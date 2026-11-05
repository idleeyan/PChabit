using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
using Windows.UI.Text;

// Block/Inline 在 XAML 与 Markdig 两个命名空间同名，用别名消除歧义：
// 裸名默认解析为 Markdig（块级源类型）；XAML 侧一律用 X 前缀别名
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using XBlock = Microsoft.UI.Xaml.Documents.Block;
using XInline = Microsoft.UI.Xaml.Documents.Inline;

namespace PChabit.App.Controls;

/// <summary>
/// 轻量 Markdown 只读渲染（3.55.0）：Markdig 解析 + RichTextBlock 原生渲染。
/// RichTextBlock 是密封类无法继承，故以附加属性形式挂接：
/// <code>
/// &lt;RichTextBlock controls:MarkdownText.Text="{x:Bind Preview}" /&gt;
/// </code>
/// 支持：标题 1–6、粗体/斜体/删除线、行内代码、代码块、嵌套有序/无序列表、
/// 任务列表、引用（嵌套）、链接、图片（异步加载，失败降级为占位文本）、表格、
/// 分隔线、单换行即换行（便签录入习惯）。
/// HTML 块/内联一律忽略（安全，不执行任何 HTML）。
/// 颜色按目标控件 ActualTheme 硬编码两套（附加属性拿不到 ThemeResource 的主题解析），
/// ActualThemeChanged 时重渲染。解析/渲染异常时降级为纯文本，内容永远可见。
/// </summary>
public static class MarkdownText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(MarkdownText),
        new PropertyMetadata(null, OnTextChanged));

    /// <summary>要渲染的 Markdown 源文本。</summary>
    public static void SetText(RichTextBlock target, string? value) => target.SetValue(TextProperty, value);
    public static string? GetText(RichTextBlock target) => (string?)target.GetValue(TextProperty);

    /// <summary>私有标志：是否已挂 ActualThemeChanged（防重复挂接）。</summary>
    private static readonly DependencyProperty ThemeHookedProperty = DependencyProperty.RegisterAttached(
        "ThemeHooked", typeof(bool), typeof(MarkdownText), new PropertyMetadata(false));

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()           // 表格 / 任务列表 / 删除线 / 自动链接
        .UseSoftlineBreakAsHardlineBreak() // 便签里单换行即换行
        .Build();

    private const string CodeFont = "Consolas";

    // 便签卡片字号偏小：h1..h6
    private static readonly double[] HeadingSizes = { 17, 16, 15, 14, 13, 12 };

    private sealed class Palette
    {
        public Brush Link = null!;
        public Brush Secondary = null!;
        public Brush Subtle = null!;
        public Brush Stroke = null!;

        public static Palette For(ElementTheme theme) => theme == ElementTheme.Dark
            ? new Palette
            {
                Link = Hex("#6CB8F6"),
                Secondary = Hex("#A0A0A0"),
                Subtle = Hex("#2D2D2D"),
                Stroke = Hex("#3D3D3D"),
            }
            : new Palette
            {
                Link = Hex("#0F5DA8"),
                Secondary = Hex("#5A5A5A"),
                Subtle = Hex("#F1F1F1"),
                Stroke = Hex("#D8D8D8"),
            };

        // 兼容 #RRGGBB（7 字符）与 #AARRGGBB（9 字符）两种写法
        private static Brush Hex(string s) => new SolidColorBrush(s.Length == 9
            ? new Windows.UI.Color
            {
                A = Convert.ToByte(s.Substring(1, 2), 16),
                R = Convert.ToByte(s.Substring(3, 2), 16),
                G = Convert.ToByte(s.Substring(5, 2), 16),
                B = Convert.ToByte(s.Substring(7, 2), 16),
            }
            : new Windows.UI.Color
            {
                A = 0xFF,
                R = Convert.ToByte(s.Substring(1, 2), 16),
                G = Convert.ToByte(s.Substring(3, 2), 16),
                B = Convert.ToByte(s.Substring(5, 2), 16),
            });
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBlock target) return;

        if (!(bool)target.GetValue(ThemeHookedProperty))
        {
            target.SetValue(ThemeHookedProperty, true);
            target.ActualThemeChanged += (_, _) => Render(target); // 主题切换重渲染取色
        }
        Render(target);
    }

    private static void Render(RichTextBlock target)
    {
        target.Blocks.Clear();
        var md = GetText(target);
        if (string.IsNullOrWhiteSpace(md)) return; // 空文本直接跳过（不取调色板）
        var pal = Palette.For(target.ActualTheme);
        try
        {
            var doc = Markdown.Parse(md, Pipeline);
            var sink = new List<XBlock>();
            RenderBlocks(doc, sink, depth: 0, pal);
            foreach (var b in sink) target.Blocks.Add(b);
        }
        catch (Exception ex)
        {
            // 解析失败降级为纯文本，保证内容永远可见；失败原因需可观测
            Log.Warning(ex, "Markdown 渲染失败，已降级为纯文本");
            target.Blocks.Add(new Paragraph { Inlines = { new Run { Text = md } } });
        }
    }

    // ---------- 块级渲染 ----------

    /// <summary>把 Markdig 块序列渲染进 sink（顶层全部为 Paragraph，复合块包 InlineUIContainer）。</summary>
    private static void RenderBlocks(IEnumerable<MdBlock> blocks, List<XBlock> sink, int depth, Palette pal)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock h:
                {
                    var p = new Paragraph { Margin = new Thickness(0, h.Level <= 2 ? 8 : 6, 0, 3) };
                    var size = HeadingSizes[Math.Clamp(h.Level - 1, 0, 5)];
                    MapInlines(h.Inline, depth, pal, inline =>
                    {
                        if (inline is Run r) { r.FontSize = size; r.FontWeight = Microsoft.UI.Text.FontWeights.Bold; }
                        p.Inlines.Add(inline);
                    });
                    sink.Add(p);
                    break;
                }
                case ParagraphBlock para:
                    sink.Add(MakeParagraph(para.Inline, depth, pal));
                    break;

                case FencedCodeBlock fenced:
                    sink.Add(AsParagraph(CodeBlockUi(ExtractText(fenced), depth, pal)));
                    break;
                case CodeBlock indented:
                    sink.Add(AsParagraph(CodeBlockUi(ExtractText(indented), depth, pal)));
                    break;

                case ThematicBreakBlock:
                    sink.Add(AsParagraph(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(0, 8, 0, 8),
                        Background = pal.Stroke,
                    }));
                    break;

                case QuoteBlock quote:
                    sink.Add(AsParagraph(QuoteUi(quote, depth, pal)));
                    break;

                case Table table:
                    sink.Add(AsParagraph(TableUi(table, depth, pal)));
                    break;

                case ListBlock list:
                    RenderList(list, sink, indent: 0, depth, pal);
                    break;

                case HtmlBlock:
                    // 忽略 HTML 块（安全）
                    break;

                default:
                {
                    // 未知块降级：取其文本行
                    var text = ExtractText(block as LeafBlock);
                    if (!string.IsNullOrWhiteSpace(text))
                        sink.Add(MakePlainText(text));
                    break;
                }
            }
        }
    }

    private static Paragraph MakeParagraph(ContainerInline? inlines, int depth, Palette pal)
    {
        var p = new Paragraph { Margin = new Thickness(0, 2, 0, 4) };
        MapInlines(inlines, depth, pal, i => p.Inlines.Add(i));
        return p;
    }

    private static Paragraph MakePlainText(string text) =>
        new() { Margin = new Thickness(0, 2, 0, 4), Inlines = { new Run { Text = text } } };

    private static Paragraph AsParagraph(UIElement ui) =>
        new()
        {
            Margin = new Thickness(0, 3, 0, 3),
            Inlines = { new InlineUIContainer { Child = ui } }
        };

    private static UIElement CodeBlockUi(string code, int depth, Palette pal)
    {
        code = code.TrimEnd('\r', '\n');
        return new Border
        {
            Background = pal.Subtle,
            BorderBrush = pal.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Child = new TextBlock
            {
                Text = code,
                FontFamily = new FontFamily(CodeFont),
                FontSize = Math.Max(11, 13 - depth),
                TextWrapping = TextWrapping.Wrap,
                Foreground = pal.Secondary,
            }
        };
    }

    private static UIElement QuoteUi(QuoteBlock quote, int depth, Palette pal)
    {
        var inner = new RichTextBlock { TextWrapping = TextWrapping.Wrap };
        var sink = new List<XBlock>();
        RenderBlocks(quote, sink, depth + 1, pal);
        foreach (var b in sink) inner.Blocks.Add(b);
        return new Border
        {
            BorderBrush = pal.Secondary,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 2),
            Margin = new Thickness(0, 2, 0, 2),
            Child = inner
        };
    }

    // ---------- 列表 ----------

    private static void RenderList(ListBlock list, List<XBlock> sink, int indent, int depth, Palette pal)
    {
        int ordinal = 0;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            ordinal++;
            string prefix;
            if (list.IsOrdered)
                prefix = $"{ordinal}.";
            else
                prefix = indent == 0 ? "•" : "◦";

            var para = new Paragraph
            {
                Margin = new Thickness(indent * 16, 1, 0, 2),
                TextIndent = -14, // 悬挂缩进：前缀悬出
            };
            para.Inlines.Add(new Run { Text = prefix + " " });

            foreach (var sub in item)
            {
                switch (sub)
                {
                    case ListBlock nested:
                        // 先落当前项段落，再递归子列表
                        sink.Add(para);
                        para = new Paragraph { Margin = new Thickness((indent + 1) * 16, 1, 0, 2) };
                        RenderList(nested, sink, indent + 1, depth, pal);
                        break;
                    case ParagraphBlock pb:
                        MapInlines(pb.Inline, depth, pal, i => para.Inlines.Add(i));
                        break;
                    case FencedCodeBlock fb:
                        sink.Add(para);
                        para = new Paragraph { Margin = new Thickness((indent + 1) * 16, 1, 0, 2) };
                        sink.Add(AsParagraph(CodeBlockUi(ExtractText(fb), depth, pal)));
                        break;
                    case HtmlBlock:
                        break;
                    default:
                        var t = ExtractText(sub as LeafBlock);
                        if (!string.IsNullOrWhiteSpace(t)) para.Inlines.Add(new Run { Text = t });
                        break;
                }
            }
            sink.Add(para);
        }
    }

    // ---------- 表格 ----------

    private static UIElement TableUi(Table table, int depth, Palette pal)
    {
        var rows = table.OfType<TableRow>().ToList();
        int colCount = rows.Count == 0 ? 0 : rows.Max(r => r.Count);

        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        for (int c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int ri = 0; ri < rows.Count; ri++)
        {
            var row = rows[ri];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool isHeader = row.IsHeader;

            for (int ci = 0; ci < row.Count && ci < colCount; ci++)
            {
                if (row[ci] is not TableCell cell) continue;

                var cellRtb = new RichTextBlock { TextWrapping = TextWrapping.Wrap };
                var sink = new List<XBlock>();
                RenderBlocks(cell, sink, depth + 1, pal);
                if (sink.Count == 0) sink.Add(new Paragraph());
                foreach (var b in sink)
                {
                    // 单元格内压缩段间距；表头加粗
                    if (b is Paragraph p)
                    {
                        p.Margin = new Thickness(4, 2, 4, 2);
                        if (isHeader)
                            foreach (var inl in p.Inlines)
                                if (inl is Run hr) hr.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                    }
                    cellRtb.Blocks.Add(b);
                }

                var border = new Border
                {
                    BorderBrush = pal.Stroke,
                    BorderThickness = new Thickness(0, 0, ci < colCount - 1 ? 1 : 0, ri < rows.Count - 1 ? 1 : 0),
                    Background = isHeader ? pal.Subtle : null,
                    Child = cellRtb,
                };
                Grid.SetRow(border, ri);
                Grid.SetColumn(border, ci);
                grid.Children.Add(border);
            }
        }

        return new Border
        {
            BorderBrush = pal.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = grid,
        };
    }

    // ---------- 行内渲染 ----------

    private static void MapInlines(ContainerInline? container, int depth, Palette pal, Action<XInline> sink)
    {
        if (container == null) return;
        foreach (var inline in container)
            MapInline(inline, sink, depth, pal);
    }

    private static void MapInline(MdInline inline, Action<XInline> sink, int depth, Palette pal)
    {
        switch (inline)
        {
            case LiteralInline lit:
                sink(new Run { Text = lit.Content.Text.Substring(lit.Content.Start, lit.Content.Length) });
                break;

            case EmphasisInline em:
            {
                if (em.DelimiterChar == '~' && em.DelimiterCount >= 2)
                {
                    // 删除线：直接把子内联包上装饰
                    MapInlines(em, depth, pal, i =>
                    {
                        if (i is Run r) r.TextDecorations = TextDecorations.Strikethrough;
                        sink(i);
                    });
                }
                else
                {
                    var span = new Span();
                    MapInlines(em, depth, pal, i =>
                    {
                        if (i is Run r)
                        {
                            if (em.DelimiterCount >= 2) r.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                            else r.FontStyle = FontStyle.Italic;
                        }
                        span.Inlines.Add(i);
                    });
                    sink(span);
                }
                break;
            }

            case CodeInline code:
                sink(new Run
                {
                    Text = code.Content,
                    FontFamily = new FontFamily(CodeFont),
                    FontSize = 12.5,
                    Foreground = pal.Link,
                });
                break;

            case LinkInline link when link.IsImage:
                sink(ImageInline(link, depth));
                break;

            case LinkInline link:
            {
                var hl = new Hyperlink();
                try { hl.NavigateUri = new Uri(link.Url ?? "", UriKind.RelativeOrAbsolute); }
                catch (Exception ex) { /* 非法 URL 降级为纯文本样式 */ Log.Debug(ex, "便签链接 URL 非法: {Url}", link.Url); }
                hl.Foreground = pal.Link;
                MapInlines(link, depth, pal, i => hl.Inlines.Add(i));
                if (hl.Inlines.Count == 0)
                    hl.Inlines.Add(new Run { Text = link.Url ?? "链接" });
                sink(hl);
                break;
            }

            case AutolinkInline auto:
            {
                var hl = new Hyperlink();
                try { hl.NavigateUri = new Uri(auto.Url, UriKind.RelativeOrAbsolute); }
                catch (Exception ex) { Log.Debug(ex, "自动链接 URL 非法: {Url}", auto.Url); }
                hl.Foreground = pal.Link;
                hl.Inlines.Add(new Run { Text = auto.Url });
                sink(hl);
                break;
            }

            case Markdig.Extensions.TaskLists.TaskList task:
                // 任务列表复选框由 TaskListInlineParser 解析为行内节点（含 Checked）
                sink(new Run { Text = task.Checked == true ? "☑" : "☐" });
                break;

            case LineBreakInline:
                sink(new LineBreak());
                break;

            case HtmlInline:
                // 忽略 HTML 内联（安全）
                break;

            default:
            {
                var t = FlattenInline(inline as ContainerInline);
                if (!string.IsNullOrWhiteSpace(t))
                    sink(new Run { Text = t });
                break;
            }
        }
    }

    private static XInline ImageInline(LinkInline img, int depth)
    {
        var url = img.Url ?? "";
        var image = new Image
        {
            MaxWidth = depth == 0 ? 210 : 180,
            MaxHeight = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 3, 0, 3),
        };
        try
        {
            image.Source = new BitmapImage(new Uri(url, UriKind.RelativeOrAbsolute));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "图片 URL 非法，降级为占位文本: {Url}", url);
            return new Run { Text = $"🖼 {FlattenInline(img)}{url}" };
        }

        // 加载失败降级为占位文本
        var holder = new InlineUIContainer { Child = image };
        image.ImageFailed += (_, _) =>
        {
            holder.Child = new TextBlock
            {
                Text = $"🖼 {FlattenInline(img)}{url}（图片加载失败）",
                FontSize = 12,
            };
        };
        return holder;
    }

    // ---------- 工具 ----------

    private static string ExtractText(LeafBlock? block)
    {
        if (block == null) return "";
        var sb = new System.Text.StringBuilder();
        var lines = block.Lines;
        for (int i = 0; i < lines.Count; i++)
            sb.AppendLine(lines.Lines[i].ToString());
        return sb.ToString().TrimEnd();
    }

    private static string FlattenInline(ContainerInline? container)
    {
        if (container == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var child in container)
        {
            switch (child)
            {
                case LiteralInline lit:
                    sb.Append(lit.Content.Text.Substring(lit.Content.Start, lit.Content.Length));
                    break;
                case CodeInline c:
                    sb.Append(c.Content);
                    break;
                case ContainerInline nested:
                    sb.Append(FlattenInline(nested));
                    break;
            }
        }
        return sb.ToString();
    }
}
