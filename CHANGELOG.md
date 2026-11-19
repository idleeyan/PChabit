# 更新日志

所有重要的更改都将记录在此文件中。

## [4.12.13] - 2026-11-19

### 设置页从「只读概览 + 4 个开关」恢复成完整可编辑设置页

- **页面**（`dist/pages/settings.html` 整页重写）：读到的每一项都按类型渲染成控件
  —— bool 勾选框、整数/小数数字框、文本输入、密码框（只显示「已设置/未设置」，留空不改）；
  分 7 组（通用 / 采集 / 网络与同步 / 备份 / 任务栏小窗 / 桌面悬浮窗 / 便签）+ 兜底「其他」，
  每组标项数，每行显示中文名 + 原始键名 + 生效标签 + 保存状态（`已保存 HH:MM:SS` / `失败：…`）。
  输入框聚焦期间跳过 60 秒自动刷新，避免打字被打断。
- **后端**（`main.rs`）：`set_setting` 语义改为「**settings.json 里已经存在的键都可以改**」，
  白名单退化为「文件里没有、允许新建」的键；新增 `SETTING_READONLY`
  （`WebDAVLastSync` / `StickyNotesLastSync` / `StickyNotesDeviceId`）拒绝改写；
  新增类型一致性校验（布尔/数字/字符串，错了直接报「类型不匹配：X 现在是 数字，传进来的是 字符串」）；
  写前备份 `settings.json.bak`、整份重写、写日志；`TaskbarEnabled` 仍即时 start/stop 小窗。
  `payload_settings` 每项补 `type`（bool/int/float/string/secret）与 `readOnly`。
- **标签口径**（照实标，不假装生效）：`立即生效` = `TaskbarEnabled` / `TaskbarShowNet|Cpu|Memory` /
  `DataRetentionDays`（下一轮清理读）；`重启生效` = 启动时读一次的项（主题、桌面悬浮窗开关与位置）；
  `Rust 未接入` = 旧 WPF 功能还没在 Rust 侧接线的键（WebDAV、备份、浏览器同步、便签同步/快捷键、
  采集间隔/空闲阈值、键盘鼠标开关等）——能改会写盘，但当前壳/采集不读它。
- 实测渲染（无头 Edge + 真实 settings.json 注入）：66 项 / 7 组，控件与标签正确。
- `app.html`：导航「设置概览」→「设置」，页元信息改「N 项设置 · 可直接修改（改完立即写回 settings.json）」。

## [4.12.12] - 2026-11-19

### 托盘图标左键单击没反应 → 打开主窗口

- **原因**：托盘只注册了 `on_menu_event`（右键菜单），并设了 `show_menu_on_left_click(false)`，
  左键既不开菜单也没有任何处理器，等于点了白点。
- **改法**：加 `on_tray_icon_event`，识别 `TrayIconEvent::Click { button: Left, button_state: Up }`
  → 主线程调 `tray_show(app, "托盘左键")`：有 `main` 窗口就 `show + unminimize + set_focus`，
  没有就 `make_window` 现建一个。
- 顺手把原来菜单「显示」里的同一段逻辑抽成 `fn tray_show(app, from)`，两处共用，日志分别记
  「[壳] 托盘左键：…」/「[壳] 托盘菜单：…」。
- 验证：`PChabitShell.exe --cmd SHOW` 走同一套亮窗逻辑，日志出现「第二实例请求：已有窗口已亮出来」，
  主窗口 `MainWindowTitle` 正常；左键点击请在托盘上实测。

## [4.12.11] - 2026-11-19

### 去掉「每小时活动」页（与仪表盘的 24 小时活跃重复）

- 导航栏删掉「每小时活动」，`app.html` 里的 `ASSET` / `TITLE` / `feed()` 分支同步移除；
- 壳侧清掉这条路径：`page_data` 的 page→文件映射与载荷分支、`all_pages()` 里的
  `"activity": payload_activity()` 都删了；`payload_activity()` 函数整体删除
  （`current_hour()` 暂留并标 `allow(dead_code)`）；
- `build.rs` 不再把 `assets\Dashboard\activity-trend.html` 复制进 `dist\pages`，
  已生成的 `dist\pages\activity-trend.html` 删除 → 发布包里不再有这个页面。
- 24 小时活跃视图保留在**仪表盘**（`dashboard.html` 的「24 小时活跃」分节，按会话开始时间累计分钟），
  信息不丢，只是不再重复出现一处。

## [4.12.10] - 2026-11-19

### 分类构成（饼图）右侧文字看不清

- **根因**：`pages/piechart.html` 的 `html, body` 写的是 `background: transparent`。在壳里当 iframe 用还能透出深色底，
  但单独打开这一页（或宿主底色不是深色）时画布是白的；图例文字又按 `prefers-color-scheme: dark` 取近白色
  `#e8e8e8`、字号只有 12 → 白底白字，几乎认不出来。
- **改法**：页面自己铺深色底 `#0f172a`（连同空状态卡片一起），深浅判断固定为深色（不再跟系统 preference 走，
  避免系统浅色时又变成深字画深底）；图例字号 12 → 13、颜色改 `#f1f5f9`、色块 14px、行距 9、`lineHeight: 18`。
- **验证**：用真实分类数据（AI 助手 16h55m / 娱乐 16h25m / … / 设计 0m 共 11 项）注入页面，
  Edge 无头渲染确认：深色底 + 近白 13px 图例文字，左侧环形图正常。

## [4.12.9] - 2026-11-19

### 设置页改完即时生效（不用重启）

- **原来**：设置页只是把值写进 `settings.json`，谁都不重读，改完要重启壳/采集宿主才生效。
- **现在**：
  - `TaskbarEnabled`：`set_setting` 里直接 `taskbar::start/stop`，勾掉立刻把任务栏小窗摘掉，勾上立刻挂回去；
  - `TaskbarShowCpu / TaskbarShowMemory / TaskbarShowNet`：小窗每拍（1 Hz）重读设置，勾掉哪项 1 秒内就从任务栏消失，
    网速关掉时 CPU/内存的竖分隔线自动跟着调整；
  - `DataRetentionDays`：采集宿主改成**每次清理前**重读保留天数，改完下一次清理就按新天数切；
  - 保存后页面自动重拉一次概览，输入框与只读区一起刷新。
- **采集间隔 / 空闲阈值**：Rust 采集是事件钩子驱动，这两项是旧 WPF 定时轮询参数，已从可编辑区移走并注明不再生效。
- **顺带**：`taskbar::start` 加了 `RUNNING` 幂等保护（重复开不会插出两个小窗）；`stop()` 走 `WM_CLOSE` 让消息循环退出。
  单实例通道新增控制指令 `TASKBAR_OFF/TASKBAR_ON`（`PChabitShell.exe --cmd TASKBAR_OFF`），
  本机实测：OFF → 日志「已按设置停止任务栏小窗」，ON → 「已创建并挂载任务栏（Rust）」。
- **日志**：`log_line` 加进程内互斥，多线程同时写日志不会再出现两行字交错。

## [4.12.8] - 2026-11-19

### 托盘「新建便签 / 便签列表」从「看着没反应」改成真有用

- **原来的行为**：两项都只是 `open_window_at("app.html?p=notes...")` —— 先关掉现有窗口再重建一个；
  「新建便签」只多了一个 900ms 后聚焦便签页输入框的动作，**并不真的建一张便签**，
  而且每次点都会把仪表盘窗口关掉重开，所以看上去像没有用途。
- **现在**：
  - 「新建便签」= 真的写库建一张空白便签（新 `pchabit_datad::notes_create_blank`，
    原来的 `notes_create` 仍拒绝空内容）→ 错开摆放位置（260,200 起每次 +26px）→
    标记为桌面便签 → 直接弹出悬浮便签窗并聚焦，窗口一出来光标就在文末，可以直接打字。
    日志：`[壳] 托盘：已新建桌面便签 <id>`。
  - 「便签列表」= 已有窗口就复用（show + `location.href='app.html?p=notes'`），
    没有窗口才新建，不再把仪表盘窗口关掉重开。日志：`[壳] 托盘：便签列表（复用已有窗口）`。
- **顺带**：`pages/note-desktop.html` 载入完内容后自动 `focus()` 并把光标放到文末。
- **排障入口**：`pchabit-datad` 新增两个子命令 —— `notes-new`（建一张空白便签并打印 id）、
  `notes-rm <id>`（软删），配合 `PCHABIT_DB` 可对库副本操作。
  验证时误对生产库跑了两次 `notes-new`，产生的两张空便签已用 `notes-rm` 清掉（便签总数回到 4 条）。

## [4.12.7] - 2026-11-19

### 应用详情页修好 + 任务栏小窗不再显示沙漏光标

- **详情页「显示异常」根因**：`pages/detail.html` 里有两个同名函数——
  详情绘制 `function renderDetail(d, a)` 和列表入口 `window.renderDetail = function (payload)`。
  后者给 `window` 赋同名属性，把前者覆盖掉了。于是点开一个应用后
  `.then(renderDetail(d, a))` 实际调用的是**列表入口**，它拿 detail 载荷去取 `payload.apps.items`
  取不到 → `APPS=[]` → 左侧列表被清空成「没有匹配的应用」，右侧 KPI / 24 小时热度 / 日趋势 /
  运行画像全部不填，副标题一直停在「加载中…」。
  **改法**：详情绘制函数改名 `paintDetail(d, a)`（附注释说明为什么不能重名），
  `.then` 改成 `window.__DETAIL_RESULT__ = paintDetail(d, a)`。
- **验证**：用真实库数据（`pchabit-datad detail` + `appcats` 输出）拼一个注入版页面，
  Edge 无头渲染截图确认：左侧 30+ 应用列表、6 个 KPI（总时长 9h1m / 会话 263 / 最长 57 分钟 /
  平均 2 分钟）、24 小时热度、每日趋势柱、运行画像（CPU 40.5% / GPU 61.7% / 72 °C）、
  窗口标题 Top 5 全部正常。
- **沙漏光标**：任务栏小窗的 `WndClassW.h_cursor` 一直是 0，且不处理 `WM_SETCURSOR`，
  鼠标移上去会沿用父窗口（explorer 任务栏）的忙碌状态光标。
  改法：注册窗口类时 `h_cursor = LoadCursorW(0, IDC_ARROW)`，并在 `wnd_proc` 里处理
  `WM_SETCURSOR`（0x0020）显式 `SetCursor(arrow)` 后返回 1。

## [4.12.6] - 2026-11-19

### 任务栏小窗改成「网速优先」布局

- **顺序**：`↓ 下行` → `↑ 上行` → 竖分隔线 → `CPU` → `内存`。网速排最前，和 CPU/内存两组分开。
- **强化下行**：下行的箭头标签与数值用 21pt 粗体（`NET_PT`），上行仍是 16pt；标签字体跟着数值字号走，
  免得大数字旁边配个小箭头。
- **分隔线**：`Segment.divider_before` 标记，绘制时在竖直 18%–82% 区间画 1px 竖线（浅色 `#B0B4B8` /
  深色 `#5A5E62`），宽度和留白由 `DIVIDER_W` / `DIVIDER_GAP` 控制。
- **量宽同步**：`remeasure()` 改成按每个 segment 自己的字号量（含分隔线宽度），窗口宽度不会因为
  放大下行而算错。
- **实测**：重启后 `[小窗] 启动 Rust 任务栏小窗（浅色 主题）` + `已创建并挂载任务栏（Rust）`。

## [4.12.5] - 2026-11-19

### 任务栏小窗透明色键改为「从任务栏实际背景采样」

- 上一版把配色换成浅色后，底色仍用写死的 `#D2D2D3` 当色键。Win11 任务栏是亚克力、
  会随壁纸和透明度变色，写死的值对不上就会在小窗周围留一块灰底。
- 现在建窗时先用 `GetDC(Shell_TrayWnd)` + `GetPixel` 在任务栏左端垂直居中取一个像素，
  把它当色键（挂上父窗口之前取，否则取到的是小窗自己）；取不到才退回按主题写死值。

## [4.12.4] - 2026-11-19

### 修「任务栏小窗字看不清」：配色跟错了主题，字号也太小

- **根因**：小窗的深浅取的是应用内 `settings.json` 的 `CurrentTheme`（你的设置是 dark），
  但 Windows 任务栏是浅色（`SystemUsesLightTheme=1`）。于是深色配色的近白色文字
  （标签 #C8C8C8、数值 #F8F8F8、网速 #85B7EB）画在浅灰任务栏上，肉眼几乎读不出来。
- **改法**：新增 `taskbar::system_uses_light_theme()`，直接读
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme`
  （原生 `RegOpenKeyExW/RegQueryValueExW`，不引依赖），并且**每次重绘都重新读**，
  用户在系统里切换深/浅色后小窗不用重启就会跟着变，日志记一行「任务栏主题切换：浅色/深色」。
- **字号**：标签 11pt→12pt、数值 15pt→16pt（量宽度和绘制共用 `LABEL_PT/VALUE_PT` 常量）。
- **实测**：重启后日志 `[小窗] 启动 Rust 任务栏小窗（浅色 主题）`。

## [4.12.3] - 2026-11-19

### 修「桌面上挂着 pchabit-netd.exe 黑窗」+ 任务栏小窗改由设置决定

- **黑窗**：壳用 `Command::new("pchabit-netd.exe").spawn()` 拉起 netd 时没给
  `CREATE_NO_WINDOW`，控制台子系统的 netd 就会带出一个黑窗口（标题是它自己的路径）。
  现在 spawn 时加 `creation_flags(0x0800_0000)`；`datad xlsx`（导周报）子进程同样加上，
  导出时不再闪窗。
- **任务栏小窗**：以前只有显式 `--taskbar` 才启动，`settings.json` 里的 `TaskbarEnabled`
  被忽略，所以设置页开着「任务栏小窗」却看不到东西。改为
  `--taskbar` **或** `TaskbarEnabled=true` 任一成立就启动；`--desktop` / `DesktopWidgetEnabled`
  同口径。
- **实测**：重启后日志 `[小窗] 启动 Rust 任务栏小窗（深色 主题）` +
  `[小窗] 已创建并挂载任务栏（Rust）`；`Get-Process pchabit-netd` 的 `MainWindowTitle` 为空。

## [4.12.2] - 2026-11-19

### 修「图标是空白」：根因是 icon.ico 本身几乎全透明

- **排查**：`(Get-Item exe).VersionInfo` 显示 ProductName/FileVersion 都在，说明 tauri-build 的
  `resource.rc` 确实把图标写进了 exe；用 `System.Drawing.Icon` 读出来逐像素看，32×32 那张
  中心 alpha 只有 34、边角 14 —— **图标文件本身是接近全透明的白团**，所以在资源管理器、
  任务栏、窗口标题栏上全都像空白。
- **改法**：用 `assets\Square150x150Logo.scale-200.png`（300×300 的真实图表 logo）重新生成
  `icons\icon.ico`，内含 16/24/32/48/64/128/256 七种尺寸、PNG 压缩帧，共 31 KB；
  原来那个 4 KB 的单尺寸近透明图标备份在 `%TEMP%\icon_old.ico`。
- **配套**：`make_window` 给窗口设 `set_icon`（bundle 关掉时 Tauri 不会自动带窗口图标，
  标题栏/任务栏会是系统默认的白纸）；`publish\Assets\PChabit.ico` 一份，桌面与任务栏快捷方式
  的 `IconLocation` 指到它；`ie4uinit.exe -show` 刷一次图标缓存。
- **验证**：`ExtractAssociatedIcon(exe)` 读出 32×32、中心 alpha=255；快捷方式图标路径指向新 ico。
- 注：任务栏固定项如果仍显示旧空白图标，取消固定再重新固定一次即可（Windows 缓存的固定项图标）。

## [4.12.1] - 2026-11-19

### 修「点了快捷方式没反应」：单实例被占用时把已有窗口亮出来

- **现象**：删掉 WPF 后桌面/任务栏快捷方式仍指向已删除的 `PChabit.exe`；就算改指 `PChabitShell.exe`，
  因为开机自启的那个进程已经占着 `127.0.0.1:18765`，第二个进程会直接退出，用户看到的就是「点了没反应」。
- **改法**：单实例端口从「纯互斥」升级成「一份通道」——第二个实例绑不上端口时，连上去发 `SHOW`，
  第一个实例收线程在主线程里 `show + unminimize + set_focus`；没有主窗口（headless 起的情况）就现建一个。
  端口同时也不再被浪费。
- **配套**：桌面快捷方式、任务栏固定项都改指 `D:\Tool\PChabit\Shell\PChabitShell.exe`
  （工作目录 `…\Shell`，图标取 exe 自带）；`启动` 文件夹里指向 WPF 的 `PChabit.lnk` 删掉，
  开机由计划任务「PChabit 采集宿主」负责，避免两份自启。
- **实测**：生产壳在跑时手动再启动一次 → 日志
  `已有实例在跑（127.0.0.1:18765 被占用），已请求它把窗口亮出来，本次退出` +
  `第二实例请求：已有窗口已亮出来`，进程仍是原来那一个（Id 未变）。

## [4.12.0] - 2026-11-19

### 迁到纯 Rust：C# 层整树删除（5.4 万行），建表与桌面便签补齐

这是一次「不再双轨」的断点：仓库里从此只有 Rust，没有 .NET。

- **建表搬到 Rust**：`rust/pchabit-collect/src/schema.sql` 是从生产库 `sqlite_master` 导出的原样 DDL
  （29 张表 + 43 个索引）。`Store::open` 会逐句执行并忽略「已存在」，所以老库照常、空库也能自建。
  实测空库跑 6 秒后 `sqlite_master` 里正好 29 张表，与生产库同名同序。
- **桌面便签搬进壳**：新增 `pages/note-desktop.html` + 四个命令（`notes_desktop / note_get / note_pos / note_desktop_set`）。
  无边框、不进任务栏、钉住置顶；拖标题栏移动、缩放后自动回写 `StickyNotes.WindowLeft/Top/Width/Height`；
  便签页每条多一个「放到桌面」按钮。实测（副本库）→ `[便签] 桌面窗口已建 a6c6586f…`、
  `[便签] 桌面便签 1 张`、`页面加载完成 [note-a6c6586f…]`。
- **删掉的代码**：`src/`（PChabit.App / Application / Core / HardwareMonitor / Infrastructure / Tests，
  306 个文件、54,606 行）、`PChabit.sln`、`Directory.Build.props`、`global.json`、`audit_baseline.*`。
- **发布形态**：`publish\` 只剩 4 个 exe + `Assets\`（约 20 MB）。`PChabit.exe` 与全部 .NET DLL 从
  `D:\Tool\PChabit` 一并清走，`.WebView2` 缓存目录保留（壳自己也要用 WebView2）。
- **图表资产归档**：原本从 `src\PChabit.App\Assets` 复制的 5 个 echarts 资产挪进 `rust/pchabit-shell/assets/`，
  `build.rs` 首选项改为该目录（`publish\Assets`、`D:\Tool\PChabit\Assets` 作为回退）。
- **验证**：四个 crate `cargo build --release` 全过；壳按计划任务启动后 `采集宿主启动 / netd 已拉起 /
  键鼠钩子安装 / 浏览器同步就绪 豆包浏览器 v2.2.4`，`dashboard 已渲染`。

## [4.11.1] - 2026-11-19

### 删除 CPU 温度采样（HVCI 环境下注定读不到，留着只会显示空值）

- **背景**：内存完整性（HVCI）开着，LHM 的 WinRing0 驱动被拦，`CpuTempMax` 自 2026-09-28 之后
  一直为空、`SensorFlags` 永远缺 `1<<1`（8189 而非全量 8191）。诊断过两条修法（关 HVCI / 换 PawnIO），
  都不划算——于是直接把这项功能删掉，而不是留一个永远空着的指标。
- **删掉的东西**：`HardwareSampleWriter` 的 `CPU.Temp` 累加与 `_cpuTempMax` 字段（写入恒为 `null`）；
  `HardwareValueProvider` 的 `case "CPU.Temp"` 改为恒返回空；`ComponentProcessor.GetCpuTemp()`
  与 `_cpuTempSensorsCache` 整体删除；WPF 硬件页的 CPU「温度」行 + 温度进度条、阈值提示里的
  「CPU 温度 70/85°C」一并去掉。
- **保留的东西**：`HardwareSamples.CpuTempMax` 列、`HardwareSensorFlags.CpuTemp` 位都留着，
  历史行不动（`2026-09-28` 之前的读数仍可查）；`SensorMatcher` 的原始传感器名识别不动，
  免得影响 GPU/主板温度那条链路。GPU 温度、显存、风扇等全部照旧。
- **验证**：`dotnet build -c Release` 通过；`dotnet publish -o publish` 出新的 `PChabit.exe`；
  壳照常起（`PChabitShell` + `pchabit-netd`），硬件页无 CPU 温度卡片。

## [4.11.0] - 2026-11-19

### P5 采集与壳合一：开机自启跑壳单进程（窗口 + 托盘 + 采集）

- **启动模型收敛**：计划任务「PChabit 采集宿主」的参数由 `--host --headless` 改为 `--host`，
  登录后直接起**一个** `PChabitShell.exe`：带窗口、带托盘、带采集宿主，不再需要
  `PChabit.exe`（WPF）在托盘里陪跑。
- **WPF 自启下线**：删除 `HKCU\…\Run\PChabit`（原值 `"D:\Tool\PChabit\PChabit.exe"`），
  WPF 版二进制仍留在 `D:\Tool\PChabit\PChabit.exe`，可手动打开做对照，但不再随开机启动。
- **踩坑**：`schtasks /change /TR "\"path\" --arg"` 的引号会多出一层反斜杠被拒；
  `Set-ScheduledTask` 又因任务 `RunLevel=Highest` 需要管理员而「拒绝访问」。
  最终用一次性 UAC 提权（`Start-Process pwsh -Verb RunAs` 跑脚本）才改成功。
- **每日对账任务**：新增计划任务「PChabit 每日对账」，00:10 跑
  `pchabit-collect --compare <昨天>`，结果追加到
  `%LOCALAPPDATA%\PChabit\Logs\compare.log`，给双轨观察期留证据。

实测（通过计划任务启动）：`托盘已建立（窗口模式）：关窗隐到托盘` / `采集宿主启动` /
`netd：已拉起新实例` / `键鼠钩子：键盘 + 鼠标均安装成功` / `浏览器扩展握手成功（豆包浏览器 v2.2.4）` /
`页面加载完成 [main]`；进程只有 `PChabitShell` + `pchabit-netd` 两个。
对账首跑 `2026-11-18`：键数 1375/1375、点击数 5777/5777、活跃分钟 391.3/391.3，结论一致。

## [4.10.10] - 2026-11-19

### 键鼠进程名口径统一 + 内核健康度进设置页 + 历史乱码清理

- **口径统一**：WPF 的鼠标会话写 `xxx.exe`、键盘会话写 `xxx`，同一应用在图表里被拆成两行。
  `pchabit-collect` 的 `input::flush` 现在统一去扩展名（大小写不敏感），两侧同源。
- **历史数据**：一次性合并脚本把 `MouseSessions` 里 1065 条 `.exe` 行改写成去扩展名形式
  （无主键冲突、无重复行），合并前把整库备份到
  `%LOCALAPPDATA%\PChabit\Data\Backups\before_proc_norm_20261119_190018.db`。
- **内核健康度**：设置页首行新增「采集内核（netd）」，直连命名管道问 `HEALTH`，没连上显示
  「未连接（守护进程没起）」——设置页从 66 项变 67 项。
- **历史乱码**：`CHANGELOG` 3.21.1 及更早条目正文在早期提交中被按 GBK 落盘损坏（`锛/銆/鍙` 一类），
  无法逐条复原，统一改为「仅保留版本号与日期 + 一句说明」；`PChabit.App.csproj` 里 8 段
  同类乱码 XML 注释整块删除，XML 解析校验通过。

实测：`MouseSessions` 近两日已无 `.exe` 后缀行；`settings` 页日志 `67 项（1 项敏感已打码）`。

## [4.10.9] - 2026-11-19

### 应用搜索下沉到数据层

- `datad` 新增 `search_json(db, q)` 与 CLI `pchabit-datad.exe search <关键字>`：进程名/应用名
  大小写不敏感包含匹配；裸参数现在也认（此前只认 `--db/--date`，`search chrome` 会退化成全量）。
- 壳新增命令 `app_search(q)` 直连数据面。
- 分类页：进程数 > 300 时把过滤交给 `app_search`（220ms 防抖，结果打 `__remoteSearch` 标记，
  本地不再二次过滤），小表继续本地过滤——两条路径结果一致。

实测：`pchabit-datad.exe search chrome` → `{"count":1,"items":[{"appName":"Google Chrome","processName":"chrome.exe"}]}`；
壳内自测钩子 `?qtest=chrome` 日志 `qtest 1 条 / chrome`。

## [4.10.8] - 2026-11-19

### 桌面硬件悬浮窗 Rust 化

- 新增 `rust/pchabit-shell/src/desktop_widget.rs`：`WS_EX_LAYERED | TOOLWINDOW | NOACTIVATE | TOPMOST`
  分层窗口，`UpdateLayeredWindow(ULW_ALPHA)` 逐像素透明；GDI 先把字画进黑底 DIB（白字），
  再按亮度当 alpha 合成到圆角深色卡片（`#202329` / 92% 不透明）上，避免色键方案的边缘杂色。
- 内容每秒刷新：CPU（`GetSystemTimes` 增量）、内存（`GlobalMemoryStatusEx`）、下载/上传（`netif::NetMeter` PDH）。
- 交互：左键拖动直接改窗口位置，松手写回 `settings.json` 的 `DesktopWidgetLeft/Top/Width/Height`；
  双击经 `run_on_main_thread` 打开仪表盘；首次启动按 `SPI_GETWORKAREA` 放主屏右侧。
- 开关 `PChabitShell.exe --desktop`（默认关，避免与 WPF 面板同屏重复）。

实测：日志 `[桌面] 悬浮窗已创建 385×365 @(2175,14)`；停掉 WPF 后截图确认
「PChabit · 硬件 / CPU 23% / 内存 49% / 下载 586 B/s / 上传 1 K/s」与圆角深色卡片渲染正常
（期间修掉一个 rop 常量错误：`BLACKNESS` 应是 `0x00000042`，之前误写成 `WHITENESS` 导致卡片发白）。

## [4.10.7] - 2026-11-19

### 任务栏小窗 Rust 化 + 托盘菜单对齐 WPF

- 新增 `rust/pchabit-shell/src/taskbar.rs`：把子窗口 `SetParent` 挂到 `Shell_TrayWnd` 左侧 8px，
  色键透明（浅 `#D2D2D3` / 深 `#292829`）、灰度抗锯齿、`Microsoft YaHei UI` 11pt 标签 + 15pt 数值，
  单行显示 `CPU 40% · 内存 50% · ↓5K/s · ↑7K/s`，负载 ≥75% 转警告黄、≥90% 转红；
  CPU 走 `GetSystemTimes` 增量、内存走 `GlobalMemoryStatusEx`、网速走 `netif::NetMeter`（PDH 每秒字节），
  每秒更新、每 5 拍重新对齐任务栏（Explorer 重启后自动重新挂载），点击经 `run_on_main_thread` 打开仪表盘。
- 开关：`PChabitShell.exe --taskbar`（默认关，避免与 WPF 小窗同屏重复）。
- 托盘菜单对齐 WPF `TrayService`：显示窗口 / 新建便签 / 便签列表 / ─ / 退出采集；
  「新建便签」直接开窗到便签页并聚焦新建框。

实测（`--headless --taskbar`）：日志 `[小窗] 已创建并挂载任务栏（Rust）`，
`FindWindowEx(Shell_TrayWnd, "PChabitTaskbarWidgetRust")` 命中，窗口 `L=8 T=1392 254×48`（150% DPI）；
截图确认渲染 `CPU 40% / 内存 50% / ↓5K/s / ↑7K/s`，停掉 WPF 后无重影。

## [4.10.6] - 2026-11-19

### 历史报告页能导出 Excel

WPF 的数据管理页有「导出」，壳原来只有只读表格。现在「历史报告」页右上角「导出 Excel」直接生成工作簿：

- 壳命令 `export_weekly()`：取 `datad::reports_json`（16 周汇总）与 `dashboard_json`（近 7 天 + 今日应用），
  拼成三个工作表 `周汇总 / 近7天 / 今日应用`，交给 `pchabit-datad.exe xlsx`（stdin 传 JSON）写盘；
- 输出到 `%LOCALAPPDATA%\PChabit\Exports\周报_YYYY-MM-DD.xlsx`，日志
  `[壳] 导出周报：路径（N KB / N 行）`，页面把路径写 `__REP_RESULT__`；
- datad 二进制查找兼容两种部署形态：与壳同目录（`publish\Shell`）或上一级（`D:\Tool\PChabit`）。

实测：`周报_2026-11-19.xlsx（8 KB / 31 行）`，解包确认工作表为 `['周汇总','近7天','今日应用']`，
表头 `周/活跃分钟/日均分钟/专注分钟/专注占比%/按键/点击/应用切换/网页分钟` 与首行 `11-16 ~ 11-22` 均正确。

## [4.10.5] - 2026-11-19

### 便签在壳里可新建 / 编辑 / 置顶 / 删除

换壳剩下的功能面里，便签原来只有「只读墙」。现在壳能直接写 `StickyNotes`：

- `datad` 新增 `notes_create / notes_update / notes_pin / notes_delete`：
  逐列对齐表结构（`Id/Content/Color/SortOrder/IsPinned/IsArchived/IsDeleted/CreatedAt/
  UpdatedAt/DeletedAt/DeviceId/Version/IsOnDesktop/Window*`），改与删都会 `Version + 1`；
  删除是**软删除**（`IsDeleted=1` + `DeletedAt`），与 WPF 的语义一致，便于便签同步；
  Id 用纳秒+pid 做种子的 xorshift 生成 GUID 形状串（不引 uuid）；颜色白名单 5 色，非法值回落 yellow。
- 写连接带 3 s `busy_timeout`，宿主正在写库时不会立刻失败
  （首版误用 `PRAGMA busy_timeout` 的 `execute`，SQLite 报 `Execute returned results`，已改 `busy_timeout()`）。
- 壳新增命令 `notes_create / notes_update / notes_pin / notes_delete`，日志只记 Id、字数、颜色，
  **不把便签正文写进日志**（内容常含账号密码这类敏感信息）。
- `notes.html`：顶部「新便签（内容 + 颜色）」；每条便签有 编辑 / 保存 / 置顶 / 删除，
  写库后自动重取载荷重绘，结果写 `__NOTES_RESULT__`。

实测（副本库）：`新建 9c4a839c-…（9 字 / green）` → 修改 → 置顶 → 软删除 →
`notes: 4 条便签`，`ntest 重读：共 4 条，自测残留 0 条（应为 0）`。

### 修逐进程网络虚高（3.7× 下行 / 14× 上行）

核对今日（11-19）数据时发现：系统表 `NetworkTrafficSamples` 合计 上 437 MB / 下 3.1 GB，
进程表 `ProcessNetworkUsages` 却是 上 6.2 GB / 下 11.4 GB。原因有两层：

1. **回环流量被算成外网流量**：ESTATS 对每个 ESTABLISHED 连接都给字节数，浏览器/Electron
   与本地服务（如 Doubao、webview2 与本机进程）之间的 `127.0.0.1` 往返也被计入，
   而系统级 PDH 计数器不含回环。`netstate::tick` 现在跳过 `127/8` 与 `::1` 的连接
   （`is_loopback_v4/is_loopback_v6`）。
2. **ESTATS 增量之和可能大于系统增量**（漏连接、连接重建）：每拍把进程增量之和按系统增量
   等比收缩再写今日累计（只收缩、不放大）。

实测（修复后 4 分钟对齐窗口）：进程 上 1.3 MB / 下 8.8 MB，系统 上 3.1 MB / 下 21.0 MB，
**下行比 0.42、上行比 0.43**（此前为 3.69 / 14.26）。比率偏低是合理的：只统计 ESTABLISHED
TCP，UDP 与短连接不计。

历史脏数据一并校正：先 `VACUUM INTO` 备份到 `Backups\before_net_fix_20261119_173406.db`，
再把 11-19 各小时进程行按当小时系统值等比缩放（只缩不放），共去掉虚高 **14,269 MB**；
校正后进程 上 420.8 MB / 下 3002.6 MB，系统 上 456.0 MB / 下 3137.0 MB。

## [4.10.4] - 2026-11-19

### 修掉「发布版壳翻页必失败」的真 bug

`page_data()` 之前会先检查 `dist\pages\<页面>.html` 是否在磁盘上，不存在就返回
`缺少页面资产`。开发目录（`rust\pchabit-shell`）下 `dist` 在，自测一直正常；
但发布目录 `D:\Tool\PChabit\Shell` 只有 exe（页面已经内嵌进 exe），于是**发布版从
托盘打开窗口后一切换页面就报缺资产**——这正是「图表组件不可用」体感的来源之一。

- 去掉磁盘存在性检查，页面名非法才报错；磁盘缺文件仅在 debug 构建里打一行提示；
- 从 `publish\Shell`（无 `dist`）实测：`data 已渲染` → `page_data data: 18.8 ms` →
  重读正常，翻页链路通过。

### 壳的数据管理页能给库做备份和清理了

WPF 的「备份/清理」是换壳剩下的硬骨头之一，先把它变成壳的直接写库能力：

- `pchabit-datad::cleanup_old(days)`：判据与采集宿主 `cleanup_before` 逐表一致
  （`KeyboardSessions/MouseSessions.WebSessions/AppSessions/AppDailyStats` 按日期，
  `HardwareSamples.Timestamp` 用 `"yyyy-MM-dd 00:00"`，`DailySummaries` 不删），
  单表失败不中断；返回 `(截止日, 删除行数)`。
- `pchabit-datad::backup_now()`：`VACUUM INTO` 生成一致性快照到
  **实际库所在目录**的 `Backups\pchabit_backup_YYYYMMDD_HHMMSS.db`
  （跟着 `PCHABIT_DB` 走，自测不会污染生产备份目录）。
- 壳新增两个命令 `cleanup_data(days)`、`backup_db()`，日志
  `[壳] 手动清理：删除 N 条（截止 X，保留 D 天）` / `[壳] 手动备份：path（N MB）`。
- `dist\pages\data.html`：加「立即备份」「清理旧数据」按钮 + 状态行，
  结果同时写 `__DATA_RESULT__`，壳日志可见。

实测（副本库 199 MB）：
- `备份完成：…\Temp\Backups\pchabit_backup_20261119_164329.db（190 MB）`；
- 保留 180 天 → 删除 0 条（库内最早数据 2026-06-20，晚于截止 2026-05-23，正确）；
- 保留 60 天 → **删除 180,029 条**（截止 2026-09-20），六表 `min(date)` 全部 ≥ 截止日，
  `DailySummaries` 未受影响。

### 采集 CLI 增加 `--compare` 日汇总对账

`pchabit-collect --db <库> --compare 2026-11-18`：把库复制到 `%TEMP%`，在副本上
`aggregate_day` 重算该日，再与生产库已存的 `DailySummaries` 行逐字段比
（键数 / 点击数 / 活跃分钟 / 应用条目数），打印差值并给出「一致 / 有差异」结论；
副本与 `-wal/-shm` 用完即删。观察期对账不用再手搓脚本。

实测：`2026-11-18` 键 1375/1375、点击 5777/5777、活跃 391.3/391.3 → **一致**；
`2026-11-19`（当天，还没到零点汇总）已存 0、重算 489 键 / 796 点击 / 127.9 分，
属「当天未汇总」的预期状态。

### 壳窗口每分钟自动刷新

壳之前只在打开时取一次载荷，窗口开着数据就不动，当日常界面用会误以为「图表不刷新/不可用」。

- `app.html` 新增 `refreshLive()`：每 60 s 重取 `pages_data` 并按当前页重绘，日志
  `[页面] <页> 已自动刷新（HH:MM）`；`admin / notes / data` 三个写库页跳过，免得打断操作；
  `?dtest=` 自测模式不启用。
- 实测：`dashboard 已渲染 ok: 2h15m / 15 应用 / 516 键` → 60 s 后
  `dashboard: … 135.2 分钟 / 15 应用 / 22.6 ms` + `[页面] dashboard 已自动刷新（16:59）`。

### 设置页从「只读概览」变成可写

换壳剩下的硬骨头之一是设置：WPF 能改、壳只能看。现在壳也能改**白名单内**的项。

- 新命令 `set_setting(key, value)`：先写 `settings.json.bak` 备份，再整份重写
  `settings.json`，日志 `[壳] 设置写入 <key>：旧 -> 新（备份 …）`；
  白名单只有 17 项（保留天数 / 采集间隔 / 空闲阈值 / 各项采集开关 / 任务栏开关等），
  含 `password/apikey/token` 的敏感项一律拒绝写入。
- `settings.html` 顶部加「可编辑项」：保留天数、采集间隔（秒）、空闲阈值（分）、
  任务栏小窗开关 + 保存按钮，结果写 `__SET_RESULT__`。
- 顺带修 `page_data('settings')` 之前返回 `{settings:{…}}` 包裹层（页面需要的是扁平
  `items/count`），并把 `settings` 补进页面映射表——之前翻到设置页会报「未知页面」。
- 实测：`设置写入 DataRetentionDays：180 -> 180（备份 …settings.json.bak）` →
  `stest 重读 DataRetentionDays=180 共 66 项`。

## [4.10.3] - 2026-11-19

### 仪表盘图表不再「静默为空」

之前只能从 `[页面] dashboard 已渲染 ok: …` 看出页面渲染成功，看不出**图表有没有数据**：
`hourly` 缺失或整条全 0 时，24 小时柱仍然画成一条几乎空的基线，日志照样报 ok。

- `dist\pages\dashboard.html` 里给两个图表加自检串：`hourly` 长度不是 24、或全 0 而总时长 > 0、
  或 `trend` 长度不是 7，就把 `[hourly 长度 N]` / `[hourly 全 0 但总时长 X 分]` / `[trend 长度 N]`
  拼进渲染结果串，壳的日志直接可见；
- 用副本库实测：`[页面] dashboard 已渲染 ok: 1h59m / 12 应用 / 475 键`（**无警告**），
  同轮 `activity / timeline / category / sankey` 也都渲染成功——数据链路是通的，
  之前「每小时图表不可用」是显示层观感问题，不是数据缺失。

## [4.10.2] - 2026-11-19

### 网络分钟行不再被脏增量撑爆

排查「网络数据虚高」时找到实证：今天 13:50、14:14、14:23、14:28、14:49、14:51 六条分钟行分别是
1.5 GB / 3.1 GB / 6.3 GB / 12.7 GB / 25.5 GB / **51.0 GB 下行**（5 条 `SampleCount` 2–5、`LastUpdated` 不在整分），
把「今日下行」从约 2.8 GB 抬到 98 GB——这才是虚高的来源，不是 ESTATS 归因本身。

- **代码侧**（`host.rs::sample_loop`）：网卡计数器回绕/驱动重载会让单拍增量变成天文数字。
  现在以 **200 MB/s（约 1.6 Gbps）** 为上界，单拍增量超界即丢弃，既不进本分钟行，也不喂 netd 的 `SYS`；
  日志留一行 `网卡增量异常（上 {up} / 下 {down}，上界 {cap} 字节），本拍丢弃`，
  以后同类事故可直接定位（首版把上界写成了 200 字节/秒，实测把正常 276 KB/s 的流量也丢了，
  当分钟即修正为按字节折算）。
- **数据侧**：删掉上面 6 条无法还原真值的异常行（删前已自动备份）。修完「今日下行」从 98 GB 回到 2.76 GB，
  与 `ProcessNetworkUsages` 今日合计（5.3 GB 下行，进程级只覆盖 TCP established）量级一致。

实测（部署后）：`16:15` 分钟行 `0.96 MB 上 / 12.4 MB 下`，日志再无「网卡增量异常」；
`cargo test --release` 13 通过。

## [4.10.1] - 2026-11-19

### 只读模式把任务栏小窗点击拦截接回来

4.9.0 切默认入口时牺牲掉的那一项功能（点小窗不弹进程网速浮窗）现在恢复了。判断过两条路：

- 把命中判定搬进 Rust 宿主/壳：宿主是 headless、没有 UI，壳要复刻任务栏定位与 8 个命中区，
  错位风险高（本机 GUI 像素验收又不可信）；
- **留在 WPF**：点击拦截本质是 UI 交互（吞掉命中区的点击 → 弹网速浮窗），不是采集。

选后者：`StartUiOnlyMode` 里只装一个鼠标低级钩子（复用 `InputHookThread` 消息泵线程，
与正常模式同一条路径），`ClickInterceptor` 判定照旧走 `TaskbarWidget.HitTestScreenPoint`；
`OnMouseClick` 事件没有订阅者，键鼠统计仍只由 Rust 宿主写库，不会双计。
退出时 `App.Shutdown` 先 `MouseMonitor.Stop()` 再停钩子线程。

实测（部署后重启）：日志 `任务栏小窗点击拦截已启用（只读模式专用鼠标钩子，不采集键鼠）`；
`TaskbarWidget 已创建并挂载任务栏` + `任务栏小窗命中区就绪: 8 个区域，首个 (16,1392)-(68,1419)`；
采集侧无变化（`HardwareSamples` 15:54/15:55 行 `SensorFlags 8189`、
`AppSessions` 正常切段、`WebSessions` 抖音（视频）入库、键盘小时行照写）。
WPF 单测 **186 通过**。

### 发布包清理（无版本号变更）

- 删掉三份无人引用的 AI 运行库：`onnxruntime.dll`（21.6 MB）、`DirectML.dll`（18.7 MB）、
  `Microsoft.ML.OnnxRuntime.dll`（0.2 MB）。全仓（`src` / `rust` / `*.csproj` / `*.json`）
  grep `Onnx|DirectML|Microsoft.ML` **零引用**，属早前 AI 功能撤掉后留在发布目录的残留；
  `publish\` 与 `D:\Tool\PChabit` 两处都已删除（这两份 dll 未被进程加载，删除不影响运行）。

### 关于「CPU 温度缺失」的更正

原先把它记成「Rust 侧无 LHM」。查库后结论不同：`HardwareSamples.CpuTempMax` 最后一次 > 0 是
**2026-09-28 20:08**（那天 865 行、44–62 °C），此后 WPF 自己写的行也全是 `0.0`（`SensorFlags` 仍写 8191）。
也就是说传感器读数在迁移之前就已经断了，不是 Rust 造成的缺口；Rust 不置 `1<<1` 位反而是诚实的。
真正的待办是查 LHM 为什么读不到温度（驱动/权限/主板支持），与迁移无关。

## [4.10.0] - 2026-11-19

### 采集宿主接管 WPF 的三件「每日维护」

4.9.0 把采集切到 Rust 之后，WPF 只读模式不再跑 `DataCollectionService` 的 15 s 定时任务，
连带三件原本只在那里发生的事一起停了：**旧数据清理**、**当日搜索计数落库**、**30 天回填**。
这个版本把它们搬进 Rust 的 `daily_loop`，库不会再只增不减。

- **数据保留清理**（`store::cleanup_before`）：逐表照抄 WPF `CleanupOldDataAsync` 的判据——
  `KeyboardSessions/MouseSessions.Date`、`WebSessions/AppSessions.StartTime`、`AppDailyStats.Date`
  按 `yyyy-MM-dd` 比，`HardwareSamples.Timestamp` 按 `"yyyy-MM-dd 00:00"` 比；
  **`DailySummaries` 不删**（汇总要长期留着看趋势）。保留天数读 `settings.json` 的 `DataRetentionDays`，
  钳到 1–3650；自测可用 `PCHABIT_RETENTION_DAYS` 覆盖。宿主启动时先清一次，之后每天过零点清一次；
- **`DailySummaries.SearchCount` 有来源了**（`websync::Engine` 内计数 + `store::search_count_set`）：
  口径与 WPF `HandleSearch` 一致——**会话在场且 `query` 非空**才 +1，不存搜索词原文；
  每 30 s 随网页兜底落库一次，`count<=0` 不建行、不覆盖其它列；
- **两个 30 天回填**（`daily::backfill_app_daily` / `daily::backfill_hardware`）：
  `AppDailyStats` 全空而历史 `AppSessions` 还在时按天补（专注仍带 25 分钟门槛）；
  `DailySummaries` 里 `CpuLoadAvg` 为空/0 的日期用 `HardwareSamples` 重算那五列，
  不动其它列、不动 `SearchCount`。

实测（副本库 `%TEMP%\pchabit_test11.db`，`PCHABIT_RETENTION_DAYS=30`）：

| 观察点 | 结果 |
| --- | --- |
| 清理 | `数据清理完成：删除 312214 条 30 天前的记录（截止 2026-10-20）`；六张表早于截止日期的行全部为 0，`DailySummaries` 2026-11-17/18 行仍在 |
| SearchCount | WS 探针发两条带 `query` 的 `search` → `DailySummaries` 今日行 `SearchCount=2`（其余列 0，未被覆盖） |
| 硬件回填 | 把 11-18 的硬件列清零后重启 → `硬件日指标回填完成：1 天`，值恢复 `35.91 / 69.94 / 28.36 / 70.0 / 43.84`（与 4.5.0 对账一致） |
| 无过期数据 | 生产库（保留 180 天）启动日志 `无过期数据需要清理（保留 180 天）`，不误删 |
| 单测 | `cargo test --release` **13 通过**（新增 search 计数口径、清理只删原始表、SearchCount 不覆盖其它列三条） |

部署后 `PChabit`（只读 UI）+ `PChabitShell --host --headless`（计划任务）+ 共享 netd 形态不变，
扩展 v2.2.4 双浏览器照常连上。

## [4.9.0] - 2026-11-19

### 默认入口切换：WPF 只读 UI + Rust 采集宿主并行

这是 P5-4 的落地版本。采集不再是「谁启动谁写」，而是分成两个角色：

**采集宿主（Rust，`PChabitShell.exe --host --headless`）** 独占所有采集表写入；
**WPF（`PChabit.exe`）** 在 `settings.json` 里 `CollectInShell=true` 时退成只读界面，
只留界面、托盘、桌面小部件需要的内存采样（`HardwareMonitorService` 采样但不落库）。

配套改动：

- **开关**：`CollectInShell`（`settings.json`，默认 `false`）；环境变量 `PCHABIT_COLLECT=shell` 可临时覆盖，便于调试；
- **单写者判断改成「心跳」**：WPF 正常采集时每 60 s 写 `%LOCALAPPDATA%\PChabit\Data\.wpf-collecting`（毫秒时间戳），
  只读模式不写；宿主发现 `PChabit.exe` 在跑但心跳缺失/超过 5 分钟，就认为它不是写者，正常启动。
  解决了「只读 WPF 在跑 → 采集宿主被进程名挡住」的死结；
- **netd 支持多客户端**：原来命名管道一次只服务一条连接，WPF 的 `ProcessNetworkMonitor` 占着管子，
  宿主怎么等都是 `ERROR_PIPE_BUSY`。现在每条连接一个独立线程（`STOP` 直接结束内核进程），
  WPF 与宿主可以同时读同一份内核数据；
- **应用会话不再被自家进程粘住**：前台快照里 `pchabit*` 自家进程与「无标题 + 高度 < 120px」的工具窗
  （任务栏小窗就是这种）不算前台应用，否则会话会一直挂在 `PChabit.exe` 上、整段不切段；
- **壳单实例锁**：占 `127.0.0.1:18765` 当互斥量，第二实例直接退出，避免多份托盘与多份采集；
- **开机自启**：计划任务「PChabit 采集宿主」（登录触发、最高权限）跑
  `Shell\PChabitShell.exe --host --headless`；`HKCU\...\Run` 的 PChabit 仍负责拉起 UI。
  两者必须同权限级别，否则低权限客户端连不上高权限 netd 管道（`ERROR_ACCESS_DENIED`）。

实测（最终形态：`PChabit.exe`（只读）+ `PChabitShell.exe --host --headless`（计划任务拉起）+ 一个共享 netd）：

| 表 | 15:15 之后的结果 |
| --- | --- |
| `HardwareSamples` | 每分钟一行，`SampleCount` 60、`SensorFlags` **8189**（Rust；再没有 8191 的 WPF 行） |
| `NetworkTrafficSamples` | 每分钟一行 |
| `AppSessions` | 真实前台切段（hongguo-desktop-companion 387s / Doubao 18s / msedge 1.8s），不再粘 `PChabit.exe` |
| `KeyboardSessions` / `MouseSessions` | 15 点 20 键 / 3 击（Rust 写） |
| `WebSessions` | 扩展 v2.2.4 连到宿主 8765：抖音（视频）/ steam（浏览）/ 百度（搜索）按域名分类入库 |
| WPF 侧 | 日志 `采集已交给 Rust 宿主` + `任务栏小窗命中区就绪`，托盘与小部件照常 |

已知代价：任务栏小窗的**点击拦截**依赖 WPF 的鼠标低级钩子，只读模式下钩子没装，点击弹进程网速浮窗
暂时不可用（小窗显示本身正常）。这是切默认入口唯一牺牲掉的功能，详见 `docs/P5-collect-migration.md` §9。

## [4.8.0] - 2026-11-19

### 关窗=隐到托盘，退出必先收口

4.7.0 的托盘只在 headless 里有，窗口模式一关窗整个进程就没了——自启当常驻用必须改掉。

- 窗口模式默认也建托盘（`--no-tray` 可关）：`CloseRequested` 里 `api.prevent_close()` + `hide()`，
  采集继续跑，托盘「打开仪表盘」再唤回；只有托盘「退出采集」才真退；
- 任何退出路径（托盘、系统注销、正常关闭）都走 `RunEvent::Exit → HostState::stop()`，
  保证最后一分钟、会话、键鼠、网页会话与 netd 都收口；
- 自检开关 `PCHABIT_CLOSE_TEST=1`：10 秒后自己发一次关窗请求，用日志验证「隐到托盘」而非退出。

实测（副本库、`--host dashboard` + 自检关窗）：

| 观察点 | 结果 |
| --- | --- |
| 托盘 | `[壳] 托盘已建立（窗口模式）：关窗隐到托盘` |
| 页面 | `[页面] dashboard 已渲染 ok: 1h3m / 9 应用 / 306 键` |
| 关窗 | `[壳] 自检：发一次关窗请求` → `[壳] 关窗→隐到托盘（采集继续，托盘「退出」才真退）` |
| 进程 | 关窗后 `pchabit-shell` 26016 仍在，`pchabit-netd` 11528 在 |
| 采集不停 | 关窗后 `14:47` 起仍有分钟行（`SampleCount` 60 / 9 / 21，`SensorFlags` 8189），应用会话照常切段 |

## [4.7.0] - 2026-11-19

### 托盘：headless 形态能独立当产品壳了

4.6.0 的 `--headless` 只能后台跑采集，开不了界面、也退不干净。这版补上托盘，把「采集与壳合一」
需要的最后一环装上：

- 壳启用 Tauri 的 `tray-icon` + `image-ico`（图标复用 `icons/icon.ico`）；
- `--headless` 时建托盘，菜单两项：「打开仪表盘」「退出采集」；
- 「打开仪表盘」：没有窗口就**现建**（载荷与注入脚本现算），已有窗口则 `show` + `unminimize` + `set_focus`；
- 「退出采集」：先 `HostState::stop()`（落最后一分钟、收尾会话、刷键鼠、收尾网页会话、给 netd 发 `STOP`），再 `app.exit(0)`；
- 托盘对象 `std::mem::forget` 保活（否则 Drop 会立刻把图标摘掉）；
- 无 GUI 环境下点不到托盘菜单，所以留了自检开关 `PCHABIT_OPEN_WIN=1`：8 秒后走**同一条建窗代码**，
  用 `页面加载完成 [main]` + `[页面] dashboard 已渲染` 两条日志当验收证据。

实测（副本库、WPF 停掉、`--host --headless` + `PCHABIT_OPEN_WIN=1`）：

| 观察点 | 结果 |
| --- | --- |
| 托盘 | `[壳] 托盘已建立：打开仪表盘 / 退出采集` |
| 现建窗口 | `[壳] 自检：headless 下窗口已打开` → `页面开始加载 / 页面加载完成 [main]` |
| 页面真的画出来 | `[页面] dashboard 已渲染 ok: 54 分钟 / 9 应用 / 217 键` |
| 采集并行不冲突 | 同时刻 `[collect] 会话入库 WindowsTerminal.exe / 7.2s` |
| 全部载荷 | 16 页 645.6 ms（dashboard 5.5 ms、timeline 0.9 ms、data 17.4 ms） |

## [4.6.0] - 2026-11-19

### 采集宿主可以无窗口常驻：`PChabitShell.exe --host --headless`

切默认入口（P5-4）差的是「谁来当常驻采集进程」。这个版本补上无界面形态：

- 新增 `--headless`：壳**不建窗口**，只启动采集宿主（netd 托管、分钟桶、应用会话、键鼠钩子、
  网页会话、日汇总）；
- Tauri 在没有窗口时会因「最后一个窗口关闭」直接退出，headless 下用
  `RunEvent::ExitRequested → api.prevent_exit()` 挡掉，进程一直常驻到被结束；
- 后台形态日志仍写 `%TEMP%\pchabit-shell.log`（`[collect]` 前缀），便于无窗口验收。

实测（副本库、WPF 停掉后跑 2 分钟）：

| 项 | 结果 |
| --- | --- |
| 进程 | `pchabit-shell` 常驻，`pchabit-netd` 由其拉起（无窗口、无控制台） |
| 硬件分钟 | `14:08` 起每分钟一行，`SampleCount` 60、`SensorFlags` 8189（差 CPU 温度） |
| 网络分钟 | `14:08`、`14:09` 行，`BytesUp/Down` 与网卡读数一致 |
| 应用会话 | 前台切换按进程/标题正常切段（Marvis 81.0s、Doubao 4.5s…） |
| 网页会话 | 真实扩展（豆包浏览器 v2.2.4）自动连上并开出 14:08:42 的抖音会话 |
| 日汇总线程 | 昨日已完整，按 `incomplete` 判定跳过（未重复写） |

切换时的已知代价（写进 `docs/P5-collect-migration.md` §9）：WPF 的**任务栏小窗点击拦截**
依赖 WPF 自己的鼠标低级钩子，纯 Rust 采集后这条链路断（托盘、开机自启也还在 WPF 侧）。
所以默认入口仍是 WPF，`--headless` 是给「切过去」准备好的形态。

## [4.5.0] - 2026-11-19

### 采集面收口：日汇总（`DailySummaries` / `AppDailyStats`）也进 Rust

4.4.0 之后采集面只剩「日汇总」还留在 WPF 的每小时维护任务里。这个版本把
`DataCollectionService.Maintenance` + `Analysis` + `ActivityLabeler` 那套聚合整体搬到
`pchabit-collect`，采集宿主现在能独立写出完整的一天。

新增 `daily.rs`：

- **键鼠**：`KeyboardSessions` 按 `Hour` 汇总 `TotalKeyPresses` 与 24 小时分布；
  `MouseSessions` 三个点击列求和；
- **应用**：`AppSessions` 有效分钟（`ActiveDuration` 优先，否则墙钟）→ 活跃分钟、`TopApps` 前 10、
  专注分钟（生产力分类，映射优先再回退会话快照）、`AppSwitches`（会话数 − 1）；
- **网页**：`WebSessions`（排除 `IsLegacy`）→ 页数、总时长、活跃时长、`WebMinutes`；
- **硬件**：`HardwareSamples` 当日分钟样本 → `CpuLoadAvg` 均值、`CpuLoadP95`（最近秩法，升序第
  `ceil(0.95n)` 项）、`GpuLoadAvg`、`GpuTempMax`、`MemLoadAvg`，一律保留 2 位；
- **行为语义**：照抄 `ActivityLabeler.Label` 的九档判定（idle/meeting/game/comms/work-code/work-doc/AI/
  web/浏览器标题/系统/媒体）与 `AggregateLabelMinutes`，产出 11 个键的 `LabelMinutesJson`；
  浏览器应用会话在有网页会话时让位，避免双计；
- **夜间与首末活跃**：23:00–次日 06:00 重叠分钟、`FirstActiveTime` / `LastActiveTime`；
- **AppDailyStats**：按归一化进程名 upsert（分钟、会话数、带 25 分钟门槛的专注分钟、小时分布）。

宿主侧新增线程每 60 分钟检查一次「昨天」（启动 10 s 后先跑一轮），判定照抄 WPF 的 `incomplete`
（空壳行、缺 `FocusMinutesV2` 都会重算）；`NetBytesUp/Down` 与 `SearchCount` 不碰。
`pchabit-collect --daily <yyyy-MM-dd>` 可单独重算某天，用于对账。

**对账（副本库重算 2026-11-18，与 WPF 当天写的行逐列比）**：

| 列 | WPF | Rust |
|---|---|---|
| TotalKeys / TotalMouseClicks | 1375 / 5777 | 1375 / 5777 |
| ActiveMinutes | 391.33469 | 391.33475（差 6e-5，墙钟回退只剩毫秒精度） |
| FocusMinutesV2 | 126.76200 | 126.76197 |
| WebPages / WebMinutes | 311 / 2050.2 | 311 / 2050.2 |
| AppSwitches | 845 | 845 |
| CpuLoadAvg / P95 / GpuLoadAvg / GpuTempMax / MemLoadAvg | 35.91 / 69.94 / 28.36 / 70.0 / 43.84 | 全同 |
| NightMinutes / First / Last | 22.0 / 13:27 / 23:51 | 全同 |
| LabelMinutesJson | work-code 13.7、learn 114.8、browse-info 1799.9、browse-fun 372.7、admin 57.1、other 200.3 | 逐键相同 |
| AppDailyStats | 30 行 | 30 行，分钟差 ≤1e-4 |

对账过程中修掉两处**自己的**问题（也是照抄纪律的价值）：

1. 标签必须用**会话快照分类**（C# `BuildLabelInputs` 传 `s.Category`），不能先做映射解析——
   否则 Trae 这类「映射到办公、快照是开发」的进程会从 work-code 掉到 work-doc（实测 13.7→3.7 分钟）；
2. 进程关键词表原样照抄，不要「顺手补几个」——多补的 `trae` / `360zip` 会让 admin 与 other 各偏 0.6 分钟。

顺带修掉一个真 bug：`dt.rs` 的时区偏移用「截断到分钟」，当 `本地-UTC` 差略小于整分钟时会整体早
**一分钟**（4.4.0 里写库时间戳就早 1 分钟），改成四舍五入到最近分钟，并补了 `parse_stamp` 与用例。

## [4.4.0] - 2026-11-19

### 采集面补齐：网页会话与磁盘活动也进 Rust 采集宿主

4.3.0 的采集宿主缺两块数据：**浏览器网页会话**（WPF 里是 `WebSocketServer` + `WebMonitor` +
`DataCollectionService.Web` 那套会话引擎）和**磁盘三列**（`DiskActivityAvg` / `DiskReadMax` /
`DiskWriteMax`）。
这两个缺口都在 Rust 侧补上了，采集面已经具备替换 WPF 写入侧的能力。

新增：

- **`websync.rs`**：自建 WebSocket 服务（`127.0.0.1:8765`，可 `--ws-port` 改）。手写 RFC 6455 握手
  （`Sec-WebSocket-Accept` 用自带的 `sha1.rs` 算），支持分片拼接、mask 解码、ping→pong，非 WS 的
  HTTP GET 回一个说明页。会话语义照抄 WPF：`pageView`/`tabActivate` 开段、`pageClose`/`tabClose` 收段、
  `heartbeat`/`visibility`/`idle` 改活跃、`scroll` 记百分比、`click` 计数、`formSubmit` 置标记；
  `ActiveDuration` = 活跃累计、`IdleDuration` = 墙钟 − 活跃、`Duration` = 结束 − 开始（100ns ticks）。
  分类走 `WebsiteDomainMappings`（按 pattern 长度降序、`*.` 前缀通配）→ 关键词兜底，与
  `WebsiteCategoryService` 同口径；浏览历史落 `BrowserHistoryItems`（`Id = url|visitTime`，
  `INSERT OR IGNORE` 去重）。
- **`dt.rs`**：扩展推的 `timestamp` 是 UTC ISO，库里的时间列全是本地时间字符串，补一层
  UTC→本地毫秒（对齐到分钟）→ `yyyy-MM-dd HH:mm:ss.fffffff` 的换算。
- **`DiskMeter`**（`netif.rs`）：PDH `\PhysicalDisk(_Total)\` 的读/写字节速率与 `% Idle Time`，
  活动率 = 100 − 空闲（0~100 夹取），算进 `HardwareSamples` 的磁盘三列；`SensorFlags` 跟着置
  `1<<8 / 1<<9 / 1<<10`（实测 8189，仅缺 CpuTemp）。
- **兜底与收尾**：新增线程每 30 s 把在场会话按「未结束」写一行（进程被强杀也不丢整段）；
  `Host::stop()` 关通道后 `finalize_all` 收尾所有在场会话。

踩到的坑（Windows 特有，已修）：**accept 出来的 socket 会继承监听 socket 的非阻塞标志**，
不显式 `set_nonblocking(false)` 的话每条连接刚握手就被关掉，表现为浏览器扩展每两三秒重连一次。

实测（副本库，`pchabit-collect --db <副本> --seconds 50 --no-hooks` + 手写 WS 探针）：

- 探针 github 页：`5.0s / 滚动 55% / 点击 1 / 有表单 / 分类 开发（Mapping）`；
- 探针抖音页：`4.0s / 分类 视频（Fallback）`；
- 真实扩展（豆包浏览器 v2.2.4）同时连上并正常产会话，退出时「收尾网页会话 1 个」；
- 硬件样本 `13:09` 行：读 4.03 MB / 写 16.36 MB / 活动 0.86%、`SensorFlags = 8189`；
- 浏览历史两页均入库；WPF 测试 186 通过 0 失败。

## [4.3.0] - 2026-11-19

### 采集与壳合一：`pchabit-shell.exe --host` 自己当采集宿主

P4 把只读视图全部搬进壳之后，写入侧还留在 WPF。这个版本新增独立 crate
`rust/pchabit-collect`，把「netd 托管 + 硬件采样 + 前台会话 + 键鼠钩子 + 落库」整块接过来，
壳带 `--host` 启动时就是采集宿主（不带仍是纯只读展示）。

采集宿主干的事：

- **netd 托管**：连不上就拉起（`--exclude-name` 排除自己与壳），启动时把库里今日每进程累计
  回填给 netd，退出时发 `STOP`；
- **硬件分钟桶**：CPU（`GetSystemTimes`）、内存（`GlobalMemoryStatusEx`）、GPU（netd `GPU`）、
  网络（PDH 网卡计数器）合成 `HardwareSamples` 一分钟一行，`ON CONFLICT(Timestamp)` upsert；
- **网络账**：每拍用网卡计数器测系统上下行 → `SYS` 推给 netd（它的系统累计只能靠宿主喂）→
  `NetworkTrafficSamples` 分钟行 + `ProcessNetworkUsages` 小时行；
- **应用会话**：netd `APP` 快照，进程或标题变化即切段写 `AppSessions`（时长仍按 100ns ticks）；
- **键鼠钩子**：键盘与鼠标各一条线程 + 自己的消息循环，回调里只做内存累加，每 15 s 刷增量到
  `KeyboardSessions` / `MouseSessions`（自动重复只计 `RepeatKeyPresses`、修饰键不计、连续输入段封段）。

三个实测问题（写进 `docs/P5-collect-migration.md` §5）：

1. **netd 的命名管道一次只服务一个客户端**。多线程各连一条会静默卡死（`app_loop` 一条会话都没写），
   改成全宿主共用一条连接 + 串行化命令；
2. **netd 的逐进程 ESTATS 在本机虚高三个数量级**（30 秒报 1.9 GB 上行，网卡真实 0.34 MB）。
   宿主现在用「系统真实增量 / 逐进程合计」等比缩放，分布保留、总量对齐；
3. **退出时最后一分钟会丢**（只有 20 秒兜底的那份）。`Host::stop()` 现在留 2.2 s 让采集线程收口，
   最后一分钟的网络行与硬件行都会落库。

单写者：`PChabit.exe` 在跑时采集宿主拒绝启动（两个宿主同时写会让同一分钟出现两份增量）。
默认入口仍是 WPF 版，`--host` 为试用开关，切换步骤写在 `docs/P5-collect-migration.md` §9。

实测（详见文档 §6）：注入输入后键盘 42 键 / 鼠标 5 击 5953 px；`00:16` 分钟行 CPU 18.8%、GPU 32%、
内存 42.4%、网络上行 10.36 MB / 下行 6.69 MB；优雅退出后 netd 进程确实消失。

## [4.2.0] - 2026-11-18

### 壳补齐最后一页：仪表盘 / 网络流量 / 键鼠活动 / 历史报告 / 数据管理

P4 收尾版。这五页是 WPF 里仅剩的、还没搬进壳的只读页面，补齐后壳的 16 个页面覆盖了
WPF 的全部只读视图（写操作仍只有分类管理与便签归属 WPF 侧）。

新增 5 个 `pchabit-datad` 子命令（全部只读、不带事务）：

- `dashboard`：今日 KPI（活跃分钟 / 应用数 / 会话数 / ≥25 分钟专注段 / 首个与最近活跃时间 /
  键 / 击 / 滚动）、24 小时活跃、Top8 应用（含占比）、今日网页分钟、今日网络下行与上行、
  CPU 与 GPU 均值、GPU 峰值温度、7 天趋势、最近一次活动；
- `net`：`--date a~b`（缺省近 7 天），`ProcessNetworkUsages` 按进程聚合上行与下行、
  24 小时下行、日趋势、采样峰值；
- `input`：键盘与鼠标的 7 天合计、24 小时双序列、按键与点击各 Top10、位移（像素→米按 96 DPI 粗算）；
- `reports`：`DailySummaries` 按自然周汇总（周一为起点），含天数 / 活跃 / 日均 / 专注 /
  专注比 / 网页 / 键 / 击 / 切换；
- `data`：库文件与 `-wal`/`-shm` 体积、11 张表行数、最近 10 条备份（含文件是否还在）。

写这五个命令时对上四处口径，其中三处是**原来的取数方式会让页面显示错**的真问题：

- **键盘与鼠标的「今日」永远是 0**。`KeyboardSessions.Date` / `MouseSessions.Date` 存的是
  `2026-11-18 00:00:00`（带时间），`WHERE Date = ?1` 拿 `2026-11-18` 比永远不等。
  改成 `substr(Date,1,10)` 前缀匹配后，今日确实有 11 行 / 1294 次按键。
- **仪表盘的今日 KPI 不能读 `DailySummaries`**。今天这一行是 `ActiveMinutes=0.0`、
  `WebMinutes=0.0`、`TotalKeys=0`，`MetricsVersion` 还比昨天落后一版——日汇总要等当天结束才回填。
  所以今日网页分钟从 `WebSessions` 现算（`ActiveDuration>0 ? Active : Duration`，ticks/6e8），
  网络从 `ProcessNetworkUsages` 当天行累加，CPU/GPU 从 `HardwareSamples` 当天
  `AVG(CpuLoadAvg)` / `AVG(GpuLoadAvg)` / `MAX(GpuTempMax)` 现算；`DailySummaries` 只在 7 天趋势里用。
- **网络「峰值」被脏样本带飞**。`NetworkTrafficSamples.PeakDownBps` 近 7 天的 `MAX` 是
  `7.09e11`（约 700 GB/s），上游也到 `1.22e12`，明显不是真实带宽。卡片改成 **99 分位**
  （`ORDER BY col LIMIT 1 OFFSET floor(cnt*0.99)`），实测下行峰值 47.7 MB/s、上行 7.1 MB/s；
  近期样本本身正常（23:31 `BytesDown=17413471 / PeakDownBps=342738`，约 334 KB/s）。
- 鼠标位移 0.0 米、网络上行 72.9 GB 都是真实数据：`MouseSessions.TotalMoveDistance` 全库为 0
  （部署版的键鼠钩子不产出位移），上行大头是 `verysync`（82 GB）与 `AdguardSvc`（67 GB）。

壳侧：`app.html` 换成 16 个导航按钮，默认入口从「硬件趋势」改成「仪表盘」；五页同样走
「窗口创建时一次算好载荷 → `initialization_script` 注入 `window.__PAGES__` → 渲染期不碰 IPC」
的路径，刷新按钮重算全部 16 页。

实测（部署版）：五页各自 6~25 ms，十六页载荷 439.8 ms；仪表盘当日 385 分钟 / 28 应用 / 1306 键，
网络近 7 天下行 78.21 GB / 上行 72.98 GB / 15 进程，键鼠 114341 键 / 50008 击 / 82598 滚，
历史 16 周（最新 11-16 ~ 11-22 活跃 866.9 分钟、专注比 40.8%），数据 199.0 MB + wal 5.5 MB / 11 表 / 5 备份。
测试 186 通过 0 失败。

## [4.1.0] - 2026-11-18

### 壳补「时间线」页（第十页，带换天）

`AppSessions` 有一天的完整起止时间，但之前只能按合计看，看不出「几点在干什么」。
这版把它做成 24 小时甘特：每个应用一条泳道，块的位置就是当天的时段，颜色来自分类色板。

口径与细节：

- 相邻且间隔 ≤ 60 秒的同应用会话**合并成一段**——否则一次切窗口就碎成几百段，
  804 段原始会话压到 329 块；
- 跨零点的会话按当日边界裁剪，时长一律由 `StartTime`/`EndTime` 算，**不读 `Duration` 列**
  （`Duration` 是会话表自己的累加值，和起止时间不必一致）；
- 泳道按当日总时长倒序，最多 22 条，其余并成「其他 N 个应用」一条（块仍用各自分类色）；
- 分类取会话行的 `Category`，空则回落到 `ProgramCategoryMappings`，再空则「未分类」；
  颜色取 `ProgramCategories` 的 `Color`，缺失时按分类名给固定色。

新增 `datad timeline` 子命令与壳命令 `timeline_day(offset)`：页面上的「前一天 / 今天 / 后一天」
通过 IPC 换天重算，不重启不刷新页面。

实测：当日 28 个应用 / 332 块，CLI 3.9 ms；十一页载荷 453.5 ms（release）。

## [4.0.0] - 2026-11-18

### 清掉残留的 AI 服务链（WPF 侧）

前几个版本已经把 AI 相关**界面**删掉了（3.57 分析页「AI 深度解读/洞察」整块、3.64 设置页
AI 分区），但**服务链本身一直还在跑**：

- `AnalyticsAiService` / `AiChatService` / `AiInsightHistoryService` / `WeeklyAiInsightService`
  四个单例仍在 DI 里注册；
- `WeeklyAiInsightService.TryRunWeeklyAsync()` 在启动 2 分钟后仍会被调度——**没有界面入口，
  但只要你配过 API Key，它就会按周拿 Key 发请求**；
- 仪表盘每次进页面还会调 `RequestAiSummaryAsync()` 覆盖今日速览；
- `AppSettings` 里还有 15 个 `Ai*` 项、`AiInsightSnapshots` 实体与建表逻辑也都还在。

本版把这些全部摘掉：删 15 个文件（4 个服务 + `OpenAiCompatibleChatClient` + `SettingsService.Ai.cs`
+ `AiContextPack` / `AiInsightDeduper` / `AiInsightMarkdownExporter` / `AiPromptExportFormatter`
+ `AiInsightSnapshot` 实体 + `AiEndpointSlot`），DI、启动调度、仪表盘、设置、DbContext、
建表、导出格式列表同步清理。仪表盘「今日速览」保留纯本地规则摘要，不再有任何网络调用。

**顺带发现的坑**：`SettingsService.Ai.cs` 里混着一个与 AI 无关的 `BrowserAutoPush`
（浏览器书签自动推送），整文件删掉会让 `ISettingsService` 缺实现、编译直接失败。
已把它搬回 `SettingsService.cs` 主体。

**删除面**：`ai-prompt` 导出格式与 `/api/settings` 里的 AI 键白名单一起失效（PUT 未知键会返回 400）。
`settings.json` 里已有的 15 个 `Ai*` 键**保留原样不主动删**（避免误删用户的 API Key），
下次 WPF 保存设置时它们才会自然消失；壳的设置页仍会对这些键打码显示。

测试从 212 降到 186（删掉 26 个只测 AI 解析/哨兵/历史的用例），全绿。

## [3.99.0] - 2026-11-18

### 撤掉「AI 洞察」页（按实际功能状态）

3.98.0 加的 AI 洞察页读的是 `AiInsightSnapshots`。用户指出 AI 相关内容在早前版本里
已经删掉了——核对属实：3.57.0 把分析页里「AI 深度解读/洞察」整块删了，3.64 把设置页的
AI 分区删了，WPF 界面早就不再展示 AI 洞察。壳里再挂一个历史快照页等于恢复已删功能，
而且库里只有 2026-09-30 的两份旧快照，没有新数据。本版连同 `datad insights` 子命令一起移除。

注：WPF 里的 AI 服务链（`AnalyticsAiService` / `WeeklyAiInsightService`）仍在注册，
只是没有界面入口。要不要把这条链本身也清掉是另一个决定，需要单独确认。

### 新增：壳补「网页访问」页（第九页）

`WebSessions` 有 15 万行，之前完全没有界面。这次把 WPF「网页访问详情」的统计口径
逐条搬到 Rust：

- **有效浏览时长**：`ActiveDuration > 0` 时用它，否则退回 `Duration`（都是 100ns ticks，
  1 分钟 = 6e8），与 C# `EffectiveMinutesValue` 一致。
- **汇总**：总访问次数、总有效时长、独立域名数、平均每次秒数、活跃时段（会话最多的小时）、
  最常访问域名（按会话数）、有效浏览率（有效分钟 / 自然分钟）。
- **小时分布**用 `Duration` 而不是有效时长——和 WPF 一样，不"修正"成看起来更合理的口径。
- **域名排行**：按有效时长 Top 20，带访问次数、时长、下行流量、占比、分类；分类解析顺序
  是「物化 `CategoryName` → `WebsiteDomainMappings`（最长 pattern 优先、后缀匹配）→
  内置关键词兜底」，逐条对齐 `WebsiteCategoryService`。
- **流量归因**：只统计 chrome / msedge / firefox 的 `BytesDown`，按「同日同小时各域名
  有效秒数」权重拆分到域名——这是估算值，页面上标了 `trafficBrowserOnly`。
- **最近访问**：按开始时间倒序 50 条，带时长文案（`X分Y秒`）与分类。

实测：7 天 5887 次访问 / 138 个域名，整份载荷 **14.3~15.8 ms**（release）。

### 修：`normalize_process_name` 的多字节 panic（真 bug）

`web` 第一次跑直接 panic：

```
thread 'main' panicked at src\lib.rs:1450:33:
start byte index 21 is not a char boundary; it is inside '工'
```

原因是这个函数按字节做后缀判断：

```rust
if p.len() >= 4 && p[p.len() - 4..].eq_ignore_ascii_case(".exe")
```

进程名以中文结尾时（`ProcessNetworkUsages` 里确实存在），`p.len()-4` 会落在多字节字符
中间，切片直接崩。**先比较再切**就没问题：

```rust
let bytes = p.as_bytes();
if bytes.len() >= 4 && bytes[bytes.len() - 4..].eq_ignore_ascii_case(b".exe") {
    &p[..p.len() - 4]   // 尾部 4 字节确定是 ASCII，切点必然在字符边界
}
```

这个函数是 detail / stats / appcats 共用的口径工具，等于所有走进程名的入口都踩着一个
中文名就崩的雷。此前的测试与实跑都只覆盖到 ASCII 进程名，所以一直没暴露。

## [3.98.0] - 2026-11-18

### 新增：壳补「便签」与「AI 洞察」两页（第九、十页）

两页都是**纯只读**，只读库里已有的数据，不发请求、不碰 API Key、不写库：

- **便签**：读 `StickyNotes`（`IsDeleted = 0`，置顶优先、更新时间倒序）。瀑布流卡片，
  按便签自身的颜色着色，正文里只把 http(s) 链接变成可点文本，其余内容一律按文本节点
  输出（不拼 HTML，不执行任何内联内容）。壳里不做编辑——WPF 负责增删改，
  两边同时写同一张表会互相覆盖。
- **AI 洞察**：读 `AiInsightSnapshots`。WPF 调完模型后把 Summary / Findings / Plan /
  Diagnosis / Risks 落库，壳只渲染历史快照：左栏按生成时间列出「本周」「近 7 天」等周期，
  右栏是摘要 + 发现（带指标证据）+ 建议（带 pending/done 状态与目标值）+ 推断
  （带置信度）+ 风险清单。

`pchabit-datad` 新增两个子命令与库函数：`notes` / `notes_json`、`insights` /
`insights_json`。历史快照里字段名不完全统一（有的 finding 没有 `Title`，有的直接叫
`Hypothesis`），渲染时逐级兜底取值，不因为早期格式缺字段而空白。

### 坑：iframe 里的 JS 语法错误是静默的

`insights.html` 第一版写完能加载、日志也说「已渲染」，但结果字符串一直是空的。
原因是页面脚本有一处括号没闭合 → 整个 `<script>` 解析失败 → `renderInsights` 根本没定义，
而壳侧 `if (w.renderInsights) w.__INSIGHTS_RESULT__ = ...` 的守卫把「没定义」当成了
「这页不需要渲染」，于是静默通过。

以后所有页面写完全部过一遍 `node --check`（把 `<script>` 内容抽出来单独检查），
六个页面现在都通过。壳侧调用也补了 try/catch，异常会写进日志而不是消失。

实测（部署后 release 版）：

```
notes: 4 条便签
insights: 2 份快照
全部载荷就绪（10 页）：389.9 ms
[页面] notes 已渲染 ok: 4 条便签
[页面] insights 已渲染 ok: 近 7 天 / 1 条
```

## [3.97.0] - 2026-11-18

### 新增：壳补「应用详情」页（第八页）

WPF 版的应用详情（会话聚合 + 键鼠切片 + 硬件归属 + 标题 Top5 + 小时分布 + 日趋势）
其实 `pchabit-datad detail` 早就全算好了（P3 落地的），一直缺个界面。这版把它搬进壳：

- **`run_detail` 拆出 `detail_json(db, req)`**，与 `stats_json` 同一套做法，壳可直接调。
- **壳新增 `app_detail` 命令**：近 7 天 vs 前 7 天，一次算完返回整份详情。
- **布局**：左栏近 7 天进程清单（带搜索，选中高亮），右栏六张 KPI 卡（总时长 / 会话数 /
  最长一次 / 平均每次 / 专注次数 / 键鼠）、24 小时分布、7 日趋势条、运行画像（CPU / GPU /
  峰值温度 / 点击 / 滚轮 / 按键）、窗口标题 Top 5。
- **进程清单只查一次**：分类管理页与详情页共用同一份 `app_categories_json` 结果，
  避免为两个页面各跑一遍 `stats`。

实测（debug 版，105 个进程）：

```
admin: 11 个启用分类（共 12 条）/ 105 个进程
全部载荷就绪（8 页）：1172.3 ms
detail 下一站江湖Ⅱ.exe: 71.2 ms / 541 分钟 / 263 会话
[页面] detail 已渲染 ok: 105 个应用
```

单应用详情 71.2 ms —— 含 7 天窗口的会话扫描 + 键鼠切片 + 硬件重叠归属。

## [3.96.0] - 2026-11-18

### 新增：Tauri 壳预览版（P4-1）随包发布

P4（UI 换壳）推进到能用的程度：Tauri 2 壳七个页面全部搬通，数据由壳内的 Rust 直接读 SQLite，
不再经 WPF 中转。壳以**预览**形式放进发布包（`Shell\PChabitShell.exe`），默认入口仍是 WPF 版。

**页面**（左侧导航，单窗口 + iframe，图表资产零改动）：

- 硬件趋势 / 每小时活动 / 分类构成 / 应用切换关系 —— 复用 WPF 的四个 HTML，只喂数据
- 活动分析 —— 壳里新写的页面（11 张 KPI 卡、近两周日历、24h 分布、分类横条、Top 变化、洞察）
- 分类管理 —— 新写的写操作页：104 个进程的分类下拉、新建分类、启用/停用分类
- 设置概览 —— 只读，敏感项（WebDAV 密码 / 两个 AI Key）自动打码，设置写入仍归 WPF 版

**壳的性能**（release，lto + strip，4.6 MB）：七页载荷冷启动 **465.7 ms**（debug 版 1189.9 ms）。

### 关键实现选择

- **数据在窗口创建时一次算完，注入 `window.__PAGES__`**，渲染阶段完全不碰 IPC。
  第一版在 iframe 的 load 回调里 `invoke(...)`，硬件/分类/桑基正常，活动页必崩
  `Maximum call stack size exceeded`——换 `postMessage`、子窗口 `eval`、try/catch 打点都无效，
  改注入后四页一次全过。
- **页面资产必须走 `WebviewUrl::App`（asset 协议）**，用 `External("file:///…")` 指 WPF 的 Assets
  能加载、`on_page_load` 也有事件，但页面里 `invoke` 永远不返回。
- **饼图补齐两个口径**：按分类（全部分类，对齐 C# `CategoryShares`）、按应用（Top 8 + 其他 `#8B5CF6`，
  对齐 `PieSlices`）。之前壳吃的是 `analytics.categoryShares`，那份会丢掉不足 5 分钟的分类
  （实测少 3 个分类、合计 4.4 分钟），饼图改走 `stats` 全量。
- **写操作口径照抄 C# `CategoryService`**：`ProcessName` 大小写不敏感 upsert、时间戳 7 位小数、
  停用只把 `IsActive` 置 0 不删行、名称在启用分类里查重。
- **`pchabit-datad` 拆成 lib + bin**，壳直接 `path` 依赖复用同一份实现；`default_db()` 支持
  `PCHABIT_DB` 覆盖，写测试全部指向库副本，真实库零写入。

### 已知边界

- 壳只读 + 分类管理写；设置写入、便签、AI 等仍只在 WPF 版。
- 双跑期尚未定：壳与 WPF 并行多久、何时切换默认入口。

## [3.95.0] - 2026-11-18

### 变更：前台窗口采样从 1 Hz 提到 500 ms（P2 尾巴）

1 Hz 的问题不是「不够准」，而是**会把短于 1 秒的切换整段并掉**：切到某应用瞄一眼再切回，
记录里根本没有这次。而且这条链路上有两处 1 Hz——内核采样与宿主读取，只提一处没用，
宿主读得比内核慢，内核采得再细也会在被读之前合并掉。

- `pchabit-netd` 采样线程：`from_secs(1)` → `from_millis(500)`。
- `AppMonitor.PollRust` 定时器：`1000 ms` → `500 ms`，与内核同频。
- 开销：平时每拍仍只有一次 `GetForegroundWindow`，只有进程名/标题变化时才解析进程与窗口矩形；
  双倍频率对 CPU 的影响在噪声级别（实测 netd 常驻 <1%）。

## [3.94.0] - 2026-11-18

### 变更：分析页计算层搬到 Rust（P3 数据面收口）

`AnalyticsEngine` 是 P3 最后一块：724 行，把 5 张表（AppSessions / DailySummaries /
HardwareSamples / WebSessions / ProcessNetworkUsages）的查询与全部聚合（KPI、日历、
小时热力、Top 变化、分类构成、洞察、多源指标、硬件画像）都做成纯计算。

- **`pchabit-datad analytics` 子命令**：自己只读查库，算完整份报告回一行 JSON（实测 180~200 ms，
  含 19.9 万行 AppSessions 的 7 天窗口）。宿主 `RustAnalyticsProvider` 只做 DTO 映射。
- **口径逐条对齐**：KPI 环比文案（`▲ 12%` / `新增` / `—`）、25 分钟专注、24 小时热力
  （`ceil(min/max*4)`）、14 天日历四档强度、Top 变化（两期字典各自大小写敏感、键并集大小写不敏感、
  ≥15 分钟、取前 5）、分类构成（≥5 分钟、前 8）、洞察全部分支与中文文案、多源指标合并、
  硬件「最长重叠会话归属」（前 12 应用）。
- **开关** `PCHABIT_RUST_ANALYTICS=0`；失败回退 C# 引擎（日志 Warn 一次）。

### 对账过程中查出的四个真问题

1. **浮点格式化不等于四舍五入**：C# `$"{x:F0}"` 自 .NET Core 3.0 起是银行家舍入（round-half-to-even），
   Rust `f64::round()` 远离零。42.5 会得到 43 而 C# 给 42。Rust 侧改用 `round_ties_even()`。
2. **TimeSpan 单位写错**：WebSessions 的 `Duration`/`ActiveDuration` 是 100ns ticks，1 分钟 = 6e8，
   第一版按 6e6 算，网页时长整体放大 100 倍（`webCategoryShares` 直接跑到 1.8 万分钟）。
3. **专注下降阈值精度**：`curFocus < prevFocus * 0.5` 是浮点比较，我第一版写成 `(prev*0.5) as i64`，
   整数截断后边界差一次。
4. **C# 侧漏查 AppName**（既有缺陷，顺带修掉）：`QuerySessionsAsync` 没选 `AppName`，
   硬件画像只能显示 `game.exe` 这类进程名。补上后与 Rust 一致，UI 显示友好名。

### 验证

新增 `AnalyticsParityTests`：同一个临时库、同一周期，分别跑 C# 引擎与 Rust 数据面，逐项对账 ——
KPI（id/展示值/环比文案/方向/说明）、分类构成、Top 变化、24 小时热力、14 天日历、
洞察 id 与完整文案、多源指标、硬件应用画像与小时桶、时段画像、数据天数。
找不到 datad 时**强制失败**，不允许静默回退。全量 **212 项通过**。

## [3.93.0] - 2026-11-18

### 变更：EPPlus 彻底退出产品依赖（P3 第三块收尾）

3.92.0 里 EPPlus 还留着当兜底，`AnalysisExcelExporter` 也仍在用它。这次两块一起收掉：
产品代码不再引用 `OfficeOpenXml`，`PChabit.Infrastructure.csproj` 删除 `EPPlus` 包引用，
**这个项目从此不再依赖任何带许可证限制的表格库**。EPPlus 只在测试项目里保留，角色变成
「第三方校验器」——用它打开 Rust 产物，确认不是「合法 zip 但内容坏」。

- **XLSX 协议升级为多区块**：一个工作表可含多个「标题 / 键值 / 表头 / 数据」段
  （`sections`），用于 `AnalysisExcelExporter` 的硬件表——它在同一张表里有两组表头
  （应用段 + 小时段）。旧的单区块写法仍兼容。
- **`ExcelExportFormatter`**：删掉 `WriteWithEpplus` 兜底，只剩「数据组织一份 + Rust 写出」。
  Rust 不可用时**明确抛错**，不再静默产出空文件。
- **`AnalysisExcelExporter`**：6 张表（概览 / KPI / 分类构成 / 应用变化 / 硬件 / 洞察）
  全部改为生成区块描述交 Rust 写出，`Export()` 签名保持同步 `byte[]`，行为对调用方不变。

### 修掉一个被兜底掩盖的真 bug

`RustXlsxWriter` 反序列化应答时没开大小写不敏感，而 Rust 返回的字段是小写
（`ok` / `bytes` / `rows` / `ms`），于是 `"ok":true` 永远绑不到 `Ok` 属性——
**3.92.0 的每一次导出其实都判定失败、悄悄回退到了 EPPlus**，日志只留一条 Warn，
线上看起来「正常」。删掉兜底后这个 bug 立刻暴露（测试报「应答不合法」）。
修法是 `PropertyNameCaseInsensitive = true`。

教训：兜底路径会把「新路径从未成功」伪装成「一切正常」，迁移期的开关要配一条
「关掉新路径必须失败」的用例，否则等于没测。

**验证**：全量 **211 项通过**；分析报告导出用例升级为断言 6 张表齐全 + 概览键值落位；
Excel 导出用例断言产物可被 EPPlus 打开，并新增「关闭 Rust 必须抛 InvalidOperationException」。

## [3.92.0] - 2026-11-18

### 变更：Excel 导出改由 Rust 写出（P3 第三块）

导出的瓶颈从来不是数据，而是 XLSX 生成：`EPPlus` 要许可证（本项目只能挂
`NonCommercial`），写出时还要逐格设置样式，8 个工作表拼下来 CPU 与内存都不便宜。

- **Rust 侧**：新增 `pchabit-datad xlsx` 子命令，用纯 Rust 的 `rust_xlsxwriter`
  生成工作簿（无 C 依赖、无许可证）。支持标题合并居中加粗、键值区块、表头灰底加粗、
  列宽、冻结表头、数值 `0.00` 格式。实测 2 张表 2.3 ms。
- **宿主侧**：`ExcelExportFormatter` 重写为「**数据组织一份，写出两套实现**」——
  `BuildSheets` 产出与语言无关的工作簿描述（名称/标题/键值/表头/行/列宽），
  主路径交给 Rust，失败回退 EPPlus（原来的逐格代码保留为兜底）。
  `PCHABIT_RUST_XLSX=0` 强制走 EPPlus。
- **列宽**：EPPlus 的 `AutoFitColumns` 换成按内容长度估算（10~50 字符夹取），
  两边观感接近，但不再需要 EPPlus 做测量。

**验证**：新增两条用例——① Rust 写出的字节必须是合法 xlsx，并且**用 EPPlus 打开一次**
确认不是「合法 zip 但内容坏」；② 关掉 Rust 后回退 EPPlus 仍能导出。全量 211 项通过。

## [3.91.0] - 2026-11-18

### 变更：应用详情切至 Rust 数据面（P3 第二块）

应用详情比应用统计重：除了会话，还要拉键鼠两张表、拉硬件分钟样本，再按「最长重叠会话」
把每一分钟样本归属到进程，最后取 CPU/GPU 均值与温度峰值。原来这条链路要开三个 try 块、
六个查询，任何一环炸掉就整卡片缺数据。

- **Rust 侧**：`pchabit-datad` 新增 `detail` 子命令，一次请求算完会话、键鼠切片、
  硬件归属、窗口标题 Top5、小时分布、日趋势强度。实测单个进程 **3.0~3.4 ms**。
- **宿主侧**：新增 `RustDetailProvider`，`AppDetailQueryService` 优先走 Rust、失败回退
  `AppDetailEngine`；开关 `PCHABIT_RUST_DETAIL=0`。
- **口径对齐**：进程匹配（原名忽略大小写 / 归一化相等）、专注阈值 25 分钟、
  标题忽略大小写分组 + 48 字截断、小时标签两位数字（`00`…`23`，**不是** `00:00`）、
  日趋势四档强度按最大日切分、硬件样本先归属再取均值/峰值、`keysPerHour` 一位小数。

**踩坑**：`HardwareSamples.Timestamp` 是**分钟粒度**（`2026-11-18 20:14`），我第一版的时间
解析要求至少 19 字符（`YYYY-MM-DD HH:MM:SS`），结果所有样本被静默丢弃——表现是详情页
「CPU/GPU」永远显示 `—`。改为兼容 `HH:MM` 无秒写法后立刻恢复（实测 41.3% / 31.3% / 70°C）。

**实测**：Doubao.exe 当日 117 段 / 56.0 分钟，与 SQL 独立计算逐项一致；全量单测 209 项通过。

## [3.90.0] - 2026-11-18

### 变更：应用统计切至 Rust 数据面（P3 第一块）

应用统计原来走「EF 拉全实体 → LINQ 聚合」：一次查询要把整段周期的会话实体全部
materialize 到托管堆，再在 C# 里分组、算环比、拼饼图。19.9 万行的库上，这条链路
每次刷新都要付一次完整 GC 压力。

- **Rust 侧**（`rust/pchabit-datad`）：独立进程持有 `rusqlite`（bundled），只读打开
  宿主正在写的同一个 WAL 库，SQL 直接按进程名聚合，算完回一行 JSON。
  协议：stdin 收 `{curStart,curEnd,prevStart,prevEnd,catFilter,search}`，stdout 回报告。
  `pchabit-netd` 仍然零 crate 依赖，发布链路不变。
- **宿主侧**（`RustStatsProvider`）：一次统计 = 一个子进程（约 20 ms），用完即退；
  找不到 exe / 超时 / 非零退出 / JSON 异常一律回退 `AppStatsEngine`，统计页不出现空白。
  开关 `PCHABIT_RUST_STATS=0`，可用 `PCHABIT_DATAD_PATH` 指定产物路径。
- **口径逐条对齐**（差异即 bug）：进程名归一化（去 .exe、小写）、分类解析优先级
  （映射表 → 会话快照 → 未分类）、兜底色板、生产力分类判定、专注会话阈值 25 分钟、
  环比文案（±5 分钟 flat / 新增 / ±N%）、饼图 Top8 + 其他、24 小时分布按 StartTime 小时。
  图表消费侧零改动：仍然拿同一个 `AppStatsReport`。

**实测**：本机 19.9 万行 AppSessions，当日 24 个进程的完整报告（查询 + 聚合 + 序列化）
3.8 ms；与 Python 独立实现同口径结果逐行一致。全量单测 208 项通过，其中新增两条
数据面回归（结构一致性、分类过滤一致性）。

**踩坑记录**：`Task.WhenAny(work, ...)` 里若再次调用 `Task.WhenAll(...)` 做比较，
拿到的永远是新的 Task 实例，判等恒为真——表现是每次都被判超时、进程被杀、静默回退。
必须先存进变量再比。

## [3.89.0] - 2026-11-18

### 变更：前台窗口监控切分至 Rust 内核（P2 第一块）

**问题**：宿主 `AppMonitor` 用 `SetWinEventHook` 监听 `EVENT_SYSTEM_FOREGROUND` 与
`EVENT_OBJECT_NAMECHANGE`，回调跑在 C# 的 STA 消息循环里——UI 线程一卡，前台切换就漏记，
表现为「应用使用时长少算」。

- **Rust 侧**（`rust/pchabit-netd/src/appmon.rs`）：独立线程按 1 Hz 轮询
  `GetForegroundWindow`，平时只有一次系统调用；只在窗口/标题变化时解析进程名、
  窗口标题、类名、矩形、是否最大化。协议新增 `APP` 命令。
- **宿主侧**（`AppMonitor`）：`PCHABIT_RUST_APPMON`（默认启用）走 Rust 通道，
  每秒拉快照、比对进程名与标题后派生事件，事件语义与钩子路径完全一致；
  内核不可用时自动回退 `SetWinEventHook`。
- **结构前置**：管道客户端从 `PChabit.HardwareMonitor` 下沉到 `PChabit.Core`
  （`DaemonPipe`）。原因是内核从网络采样扩到 GPU、前台窗口后两个程序集都要用它，
  而服务端同一时刻只服务一个客户端，必须全进程共用一条连接。

**取舍**：键鼠低层钩子不迁——每次按键都经 IPC 反而更慢，且会引入跨进程计数的
一致性风险；`WebMonitor`（浏览器历史）与 `AppInfoResolver`（版本信息/分类）暂留 C#，
前者与库表强耦合，后者依赖分类配置。

## [3.88.0] - 2026-11-18

### 变更：GPU 传感器采集切分至 Rust 内核（NVML 直读，默认启用）

**问题**：硬件监控 `UpdateAll()` 实测 86.5 ms/拍，占 App 全部 CPU 的 92%；逐节点分解后
**GPU 一个节点就占 85.73 ms（99%）**，CPU 3.13 ms、网络 0.06 ms、主板/内存合计 0.03 ms。
也就是说这一整块开销几乎全来自 LHM 的驱动级 GPU 全量采样，而 PChabit 实际只用
6 个指标（占用/温度/显存/风扇/频率/功耗）。

- **Rust 侧**（`rust/pchabit-netd/src/gpu.rs`）：运行期 `LoadLibraryW` 装 `nvml.dll`，
  `GetProcAddress` 取函数指针，零新增 crate 依赖；`Nvml` 持会话与 0 号设备，
  热指标（利用率/温度/显存）每拍读，冷指标（风扇/频率/功耗）每 5 拍读一次并沿用缓存。
  协议新增 `GPU` 命令（一行 JSON），无 `nvml.dll`/无 N 卡时 `avail=false`。
- **宿主侧**（`RustGpuProbe`）：每拍取快照，命中 `GPU.Load/Temp/VRAM/Fan/Clock/Power`
  时直接返回内核值，`HardwareMonitor.UpdateAll()` 跳过 LHM 的 `hw.Update()`；
  距上次应答超 3 秒或内核报不可用即整体失效，原样退回 LHM（不做半截数据）。
- **连接模型**：`RustNetClient` 改为进程级共享——管道服务端同一时刻只服务一个客户端
  （`serve` 循环逐个 accept），网络采样与 GPU 探针必须复用同一条连接，各建一条会互相挡住。
- **口径对齐**：`GPU.VRAM` 在 LHM 侧是「已用/总量 × 100」的百分比而非 MB，
  第一版误填 MB 会导致 UI 显示 1400% 之类，已修正为百分比并回填 `DetectedGpuVramTotalGB`。
- **开关**：`PCHABIT_RUST_GPU`（默认启用，`0`/`false`/`off` 关闭）。

**实测**（本机 i5-12490F + RTX 2060 SUPER，60 拍）：

| 路径 | 每拍耗时 |
| --- | --- |
| LHM `GpuNvidia.Update()` | 85.73 ms（p50 86.8 / max 123.4） |
| Rust NVML 探针（热/冷分级） | 1.90 ~ 2.70 ms（p50 0.61 ~ 0.87） |

**线上验证**（3.88.0 部署后稳态）：App CPU 由约 9% 单核降至 **1.56%**；
`HardwareSamples` 每分钟照常落库，GPU 负载/温度/显存与 NVML 直读一致
（19:39 行：负载均值 29.5%、温度 34℃、显存 1433/8192 MB）。

## [3.87.0] - 2026-11-16

### 变更：键盘统计过滤长按自动重复（口径修正）

**问题**：空格、W 的按键数明显虚高。根因是统计口径为「每一次 KEYDOWN 都算一次按键」，
而按住不放时 Windows 按系统重复率（默认约 30 次/秒）持续发 KEYDOWN。
实测本机数据：08 时（游戏前台）W 键 3678 次/小时、Space 2530 次/小时；
09 时 Space 2889 + W 2377 + 右方向键 1834。按 30 次/秒反推，
就是「按住 W 累计约 2 分钟」被记成了 3678 次。

- **过滤自动重复**（`KeyboardMonitor`）：维护「当前按下的键集合」，KEYDOWN 时该键已在集合中
  即判定为 auto-repeat；KEYUP 时移出。加 1500ms 兜底——超过该阈值仍收到 DOWN 说明
  KEYUP 丢失（PrintScreen 等只发 DOWN 的键），按新按下处理，避免整键被永久判成重复。
  快速连按同一字母（"ll"、"oo"）中间有 KEYUP，不会被误判。
- **重复单独计数**（A+B 方案）：`KeyboardSession` 新增 `RepeatKeyPresses` 列
  （`INTEGER NOT NULL DEFAULT 0`，启动迁移自动补列）；自动重复只累加该列，
  不进入 `TotalKeyPresses` / `KeyFrequency` / 打字速度 / 快捷键统计。
  键盘详情页「总按键次数」卡片下新增一行小字「长按重复 N 次」，无重复时不显示。
- **顺带修掉总数与分布不一致**：`vkCode <= 0` 的合成/异常输入此前只进总数、不进按键分布，
  造成「总按键 3395、Top 键合计只有 2」这类穿帮行（本机 11 时、12 时、11-15 11 时共 4 行）。
  现已在钩子入口直接丢弃。
- **限制**：历史数据无法回溯拆分——库里只存聚合结果，没有原始事件流，
  旧行的 `RepeatKeyPresses` 为 0，新口径只对升级后产生的数据生效。

### 新增：进程网络采样内核切分至 Rust（可选通道，默认启用）

**动机**：`ProcessNetworkMonitor.SampleOnce()` 本机实测 **5.208 ms/拍**（p50 4.957 / p95 7.175 /
max 9.343，40 拍 @1Hz）。这段逻辑全是裸指针结构体解析、逐连接 ESTATS 取值与进程名字符串处理——
与硬件传感器（LHM 占 App CPU 约 92%）相比它只有 5.5% 的分量，但它是整段「本可以交给 Rust」的代码。

**切口（不整体重写）**：只搬走「连接表枚举 + 逐连接 ESTATS + 进程名解析 + 速率/会话/今日聚合」；
硬件传感器、UI、落库、DI、数据库结构全部不动。

- **独立进程** `pchabit-netd`（`rust/pchabit-netd`，8 个源文件 1,934 行，零第三方依赖），
  手写 `iphlpapi` / `advapi32` FFI，MIB 结构体尺寸在编译期断言
- **通信**：命名管道 `\\.\pipe\pchabit-netd-v1`，行式文本命令 + 单行 JSON 应答；命令集为
  `PING` / `HEALTH` / `SYS` / `SEEDP` / `SEEDSYS` / `TOTALS` / `ALLTODAY` / `SNAP` / `STOP`；
  单实例由 `Global\pchabit-netd-v1` 命名 Mutex 守卫，宿主 90 秒无请求时内核自动退出
- **系统级口径不由内核自算**：宿主每秒推送「本拍字节增量」`SYS`，内核负责累加。
  不用 `GetIfTable2`——`MIB_IF_ROW2` 近 1.3 KB、位序易错，`MIB_IFROW.dwInOctets` 是 32 位会回绕；
  也不让内核按自身 elapsed 对速率积分——实测两套 1 秒时钟错拍会让系统累计偏低约 15%
- **语义逐条对齐 C# 现有实现**：只记 ESTATS 真实增量（不按连接数分摊）、进程速率和超系统
  105% 才收缩（且系统读数 > 0 才收缩）、30 秒无连接无速率即回收、256 进程上限、跨天清零、
  种子按 max 回填、排除自身 PID 与自身进程名
- **C# 侧退化为缓存层**：新增 `ProcessNetworkMonitor.Rust.cs`（289 行，`SampleRemote` /
  `ApplyRemoteSnapshot` / `BuildRemoteSnapshot` / `EnsureRustSampler`）与 `RustNetClient.cs`
  （227 行，连接、重连、拉起、回收）。公开 API、ViewModel、XAML、DI 全部零改动
- **回退**：默认启用；`PCHABIT_RUST_NETSAMPLER=0`（或 `false` / `off`）整体退回内置 C# 实现；
  内核 exe 缺失、连不上、应答超时都静默回退，只记 Debug 日志，不影响功能
- **构建**：`cargo build --release` 产出 304 KB exe，App csproj 在 build / publish 后自动拷贝
  （带 `Condition="Exists(...)"`，**不在此调 cargo**——没装 Rust 工具链的机器仍可正常构建）

**实测（同机同秒，`tools\SamplingBench`）**：

| 路径 | 每拍耗时 |
|---|---|
| C# 内置 `SampleOnce` | 5.208 ms（p50 4.957 / p95 7.175 / max 9.343） |
| Rust 内核（生产路径，注入 LHM 速率） | **0.444 ms**（p50 0.359 / p95 2.163） |

按可控对照推算净省约 4.76 ms/s ≈ 单核 0.48% ≈ 12 核整机 0.04%。
A/B 一致性：进程名集合与速率逐拍一致；系统累计存在 **1 拍结构性滞后**
（宿主推送的增量要等内核下一拍并入），末拍对比会看到 C# 先跳、Rust 后跳，不是丢数。
线上 App 的 CPU 读数不能直接与 bench 基线对比（活跃度、LHM 是否在刷新都不可控）。

**注**：本次与 3.87.0 键盘口径修正同批构建发布（部署产物同为 3.87.0.0，已含 `pchabit-netd.exe`），
未单独递增版本号；后续改动请按规则递增。

**已知限制**：内核 health 与开关未暴露到设置页，目前只有环境变量；LHM 降频
（`RefreshMs` 1000→2000/3000）未实施，它仍是 CPU 的最大头（估算可省 30–60 ms/s）。

## [3.86.1] - 2026-11-16

### 修复：进程排行恒为空（一直显示「当前没有进程在传输数据」）

**现象**：3.86.0 提权运行、日志已打出「连接级 TCP 统计已启用」，但浮窗始终没有进程行。

**根因一（主因）**：`GetPerTcpConnectionEStats` 的 P/Invoke 声明从早期版本起就**参数错位**。
真实签名是 `Row, EstatsType, Rw, RwVersion, RwSize, Ros, RosVersion, RosSize, Rod, RodVersion, RodSize`
三段各 3 个参数；原声明把 `out rod` 放在第 6 位（**Ros** 段），真正的 `Rod` 段传的是
`IntPtr.Zero/0/0`。结果调用返回 `NO_ERROR`，但 ROD 从未被写入，`rod` 结构恒为全 0 ——
所以无论是否启用采集，进程级字节永远是 0。已改为正确声明并重排实参。
（0 字节现象与「ESTATS 未启用」叠加，3.85.1 只发现了后者。）

**根因二**：`NormalizeRatesToSystem` 在系统读数为 0 时会把进程速率乘成 0
（`displayDownBps=0` → `downScale=0`）。LHM 传感器缺值归零的拍次里，实时条目会整片消失。
已改为**仅当系统读数 > 0 时才收缩**。

**顺带补齐**：
- **IPv6 TCP 归因**：`CollectTcpV6` 此前只统计连接数、不做 ESTATS。现补
  `GetPerTcp6ConnectionEStats` / `SetPerTcp6ConnectionEStats` 与 `MIB_TCP6ROW` 结构，
  连接键用地址十六进制串 + 端口，避免多行碰撞。
- **归因健康度自证日志**：首次出数记一条 Information（本拍字节/连接数）；
  连续 5/30 拍无增量记 Warning，带上「ROD 读到 N 条、其中累计非零 M 条、已尝试启用 K 条」——
  日志本身即可区分「没读到字节」与「流量根本不走 TCP」。
- **空态文案分层**：区分「未提权」「ESTATS 启用中」「已启用但近 60 秒无可归因 TCP 流量
  （UDP/QUIC 与回环无法按进程归因）」「当前确实无传输」，不再笼统提示无流量。

## [3.86.0] - 2026-11-16

### 新增：点击任务栏小窗弹出「实时网速 · 进程排行」

**需求**：点击任务栏左下角的网速监控小窗，弹出窗口展示当前占用网速的进程排序。

- **点击响应（关键约束）**：`TaskbarWidget` 是 `Shell_TrayWnd` 的子窗口，任务栏的 XAML 层压在
  它上面，**真实鼠标输入到不了它**（实测该窗口收不到 `WM_SETCURSOR`/`WM_LBUTTONUP`，只有
  `PostMessage` 能直达窗口过程）。因此点击入口改为**全局鼠标钩子拦截**：
  `TaskbarWidget` 在绘制时记录「画了文字」的命中矩形（屏幕坐标，整表替换、钩子线程无锁读取），
  `MouseMonitor` 新增 `ClickInterceptor`，在 `WM_LBUTTONDOWN` 时命中即吞掉该次点击（连带的
  `WM_LBUTTONUP` 也吞），并派发到 UI 队列开浮窗。空白像素不拦，点击照常落到任务栏控件上。
  复用了已有的输入钩子，未新增系统级钩子；命中区首次就绪会记一条日志便于排障。
- **浮窗**（`NetworkProcessWindow` + `NetworkProcessView` + `NetworkProcessViewModel`）：
  无边框置顶单例浮窗，贴在任务栏小窗正上方（任务栏在顶部时改到下方），
  `WS_EX_TOOLWINDOW` 不进任务栏/Alt+Tab，失焦或 Esc 自动收起，按行数自适应高度。
  内容为系统实时 ↓/↑（与任务栏小窗同一口径 LHM `NET.Up/Down`）+ 进程排行
  （序号/进程名/下行/上行/相对条形）+ 今日累计；1s 刷新，成员与顺序不变时原地更新，不重建列表。
- **同一点击的抖动消解**：点击小窗时浮窗会先因失焦收起，随后点击回调又试图打开；
  记 `_lastHiddenUtc`，500ms 内视为已处理，避免"点一下闪两次"。
- **进程级归因**（`ProcessNetworkMonitor`）：补齐 `SetPerTcpConnectionEStats`，启用
  `TCP_ESTATS_DATA` 连接级采集（每连接只试一次，权限不足时永久置不可用，不再产生 syscall）。
  3.85.1 记录的"只读 ESTATS 从来拿不到字节"在此收口。

**权限事实（本机实测，非推测）**：普通权限下 `GetPerTcpConnectionEStats` 返回 NO_ERROR
但 40/40 条连接字节恒为 0；`SetPerTcpConnectionEStats` 返回 **5 = ERROR_ACCESS_DENIED**；
ETW `Microsoft-Windows-Kernel-Network` 内核会话同样"拒绝访问"。即 Windows 只在管理员权限下
允许进程级流量归因。因此普通权限下浮窗**不伪造进程数据**，改为显示系统速率 + 权限说明
（「以管理员身份运行 PChabit 后即可看到排序」），提权与否由用户自己决定。

- **为什么不放"一键提权重启"按钮**：试做过 `Process.Start(Verb=runas)` + `--replace-instance`
  单实例交接，实测该路径不可靠（新实例约 14s 后才起来且仍是普通权限、交接日志未触发），
  容易让用户以为程序"自己重启了却什么都没变"。已移除按钮；需要常驻提权时应改用
  计划任务（最高权限）自启，属独立改造。
- **生命周期**：禁用任务栏小窗或应用退出时，浮窗在 UI 线程关闭（`CloseNetworkProcessWindow`）。

## [3.85.1] - 2026-11-12

### 修复：硬件监控磁盘/网络读数与进程流量归属

**问题**：网络流量页与硬件监控「最大占用进程」长期把 PChabit 自身列为流量第一。
本机实测 2026-11-12 单日记入 PChabit 20.83 GB（↑4.57 GB / ↓16.26 GB），
占进程合计 91.7%、约整机流量（23.32 GB）的 89%；近 7 天有 5 天 PChabit 居首。

**根因**：`ProcessNetworkMonitor` 在拿不到 TCP ESTATS 增量时，会把整机上下行
按 `OtherTransferCount` / 连接数加权分摊给所有出现在连接表里的进程。
项目只声明并调用了 `GetPerTcpConnectionEStats`，从未调用
`SetPerTcpConnectionEStats` 启用连接级统计，Windows 默认关闭该能力，
因此几乎每拍都走分摊兜底；而本进程持续写 SQLite/日志且常驻本地监听端口，
权重最大，整机流量被系统性地误记到 PChabit 名下。

- **修复归属**：移除凭空分摊，只记录可归因的 ESTATS 真实增量；系统残余流量
  仅计入系统会话/今日累计。排除自身进程（PID + 进程名），种子与今日分桶
  同步过滤；进程榜只显示有真实流量的条目，无数据时显示「暂无活跃网络进程」。
  顺带移除每拍打开 64 个进程句柄采集 `OtherTransferCount` 的开销。
- **读数归零**：`NET.Up/Down`、`DISK.Read/Write/Activity` 在传感器缺值时
  返回 0，不再沿用 `lastValidMap` 的上一帧；自动选优未锁定时固定一块
  真实网卡/磁盘作为每拍刷新目标，避免 LHM 传感器值冻结造成「一直有读写」。
- **进度条**：磁盘/网络满刻度由「当前峰值 × 1.25」自适应改为固定档位
  （磁盘 10/50/150/500 MB/s，网络 1/5/20/125 MB/s），消除最大项永远画到
  80% 的视觉误导。
- **历史数据**：网络流量落库服务启动时幂等清理 `ProcessNetworkUsages` 中
  自身进程的历史误记行；种子加载与流量页查询同样过滤自身，清理失败时
  页面也不会再出现 PChabit。
- **文案**：硬件监控「说明」卡片明确上下行速率为整机网卡口径，并说明
  无连接级统计时不做分摊。
- **范围**：未改动系统盘优先（+1e9）评分与 PDH `_Total` 口径；启用 ESTATS
  或接入 ETW 拿真实进程级字节属于后续独立改造。

## [3.85.0] - 2026-11-06

### 变更：移除设置页「AI」分区（用户需求）

设置页的 AI 分区不再有存在意义，整区删除。**注意：AI 功能链本身
保留**——仪表盘「今日速览」的 AI 摘要（`DashboardViewModel.RequestAiSummaryAsync`）、
每周自动 AI 解读（`IWeeklyAiInsightService`）、`AnalyticsAiService` 等仍在
运行；删除的只是配置入口。

- **删除**：`Views/Settings/AiSettingsSection.xaml(.cs)`（381 行，3.64.0
  D3 批次拆出的自治分区）、设置页侧栏「AI」导航项、宿主接线三处
  （Initialize/ApplySection.Show/Reload）。
- **保留**：`SettingsViewModel`/`SettingsService` 的 AI 属性与
  `settings.json` 既有配置（用户当前 `AiInsightsEnabled=true`，仪表盘
  AI 摘要继续生效；如需关闭可改 settings.json 或后续批次移除功能链）。
- **审计基线漂移归因**：`asyncVoidCount` 61→60（删除的
  `AiTestConnection_Click` 事件处理器）、`catchGenericCount` 436→434
  （删除文件内 2 处 catch）；其余计数零变化。
- **验证**：build 0 错误（24 警告=基线）；206/206；守卫 28/28；
  冒烟 PASS（3.85.0，0 ERR/FTL）；`smoke_ui.py` 9/9 页面 34 秒；
  设置页实测：AI 项确认消失、分区切换正常、运行日志 0 ERR/FTL。

## [3.84.0] - 2026-11-06

### 变更：工程改造计划阶段 G——测试扩面（G1/G2/G3 完成，G4 基线建立）

**测试总数 187 → 206（计划目标 ≥200 达成）。**

- **G1 迁移双路径测试（`DatabaseMigrationTests`，10 用例）**：新库
  `EnsureCreated` / 老库缺表缺列走 `DatabaseInitializer` 手动 SQL 迁移 /
  幂等重复执行不破坏 schema 与数据 / 关键列完整性（AppSessions
  Duration、KeyboardSessions KeyFrequency 等）。**测试坑**：迁移器开
  第二条独立连接，内存 SQLite 必须 `cache=shared` 且每库唯一名
  （xUnit 并行共享同一内存库会导致 EnsureCreated 假失败）。
- **G2 采集链路测试（`DataCollectionServiceTests`，9 用例）**：
  **3.26.2 回归**——同批 50 条操作含主键冲突对时，逐条隔离重试保住
  48 条合法数据（反射预灌批次，确定性无竞态）；会话切换闭合
  （StartTime=事件时间戳、EndTime=墙钟的真实语义）；键鼠桶 Upsert；
  网页会话 PageView→PageClose 落库；Stop 残余提交；幂等 Start/Stop。
  测试发现：SQLite 对空进程名无约束，空值不构成脏数据——改用同 Id
  主键冲突作为真实脏数据源。
- **G3 冒烟页面导航（`scripts/smoke_ui.py`）**：uiautomation 遍历
  9 个主页面（仪表盘/时间线/应用使用/网页访问/硬件监控/分析/便签/
  分类管理/数据管理），断言每页无新增 ERR/FTL。**实测：9/9 通过、
  全程 38 秒（<2 分钟验收达标）**。
- **G4 覆盖率基线（cobertura 实测）**：Infrastructure.Analysis
  **66.5%**（目标 70%，差 3.5pp）；Infrastructure.Data **70.6%**
  （目标 50%，达标）；Application **31.2%**（目标 60%，主体为
  DataCollectionService 六分部，随持续测试扩面推进）。
- **验证**：206/206；build 0 错误（24 警告=基线）；冒烟 PASS
  （3.84.0，0 ERR/FTL）。

## [3.83.0] - 2026-11-06

### 变更：工程改造计划阶段 F2——周期任务重入防护

**目标**（计划阶段 F2）：所有 Timer/DispatcherTimer 回调统一
`Interlocked.CompareExchange` 防重入（陷阱 27），慢一轮时下一拍跳过，
不出现任务堆积。

- **防护盘点（六补二留）**：仪表盘 30s（`_tickBusy`）与便签同步
  （`_noteSyncRunning`）已有防护保持不动；**新增六处**——
  - 桌面悬浮插件 1s `RefreshDesktopWidget`（`_widgetTickBusy`，回调内含同步阻塞点）
  - 托盘/任务栏 5s `RefreshTrayDisplay`（`_trayTickBusy`，含 async 内容链）
  - `HardwareMonitorService.Tick`（硬件采样）
  - `ProcessNetworkMonitor.TickSafe` / `ProcessResourceMonitor.TickSafe`（进程采样）
  - `BackupService.StartPeriodicBackupAsync` 的 Elapsed（**System.Timers 允许并发触发，重入高危**；慢备份含 VACUUM 叠加会放大锁争用）
- **验证**：build 0 错误（24 警告=基线）；187/187；冒烟 PASS（3.83.0，
  0 ERR/FTL）；运行期观察：悬浮插件 1s 定时器正常启动、硬件/网络采样
  按分钟桶持续落库（21:59→22:01 逐分钟递增）、进程稳定 382MB——
  防护未破坏任何周期链路，无堆积迹象。

### 变更：工程改造计划阶段 F3——Task.Run 审计（审计完成，零违规，无代码改动）

- **全量审计 47 处 Task.Run**（含 fire-and-forget `_ = Task.Run`）：
  逐项标注用途（UI→后台计算 / 后台定时 / 纯后台 IO）。
- **双层 Task.Run**：0 处（正则+块级扫描复核，两处疑似为相邻独立调用误报）。
- **跨线程 ObservableCollection**：0 处违规（WebDetails 分页构建为局部
  List 回 UI 线程填充；NotificationService 为 Dictionary 非集合绑定）。
- **COMException 0x8001010E**：3.83.0 运行段 0 条（全日日志唯一一条
  COMException 为 09:26 历史记录、F1 修复前的 ContentDialog 单开冲突，
  已被兜住记 ERR 未崩溃）。
- **结论**：F3 验收条款天然满足，本批为纯审计批次，无代码改动。

## [3.82.0] - 2026-11-06

### 变更：工程改造计划阶段 F1——async void 治理

**目标**（计划阶段 F1）：非事件处理器的 `async void` 全部改
`async Task`；事件处理器 100% 包裹 try/catch+日志，异常不再直达
App 级崩溃。

- **非事件处理器改造（2 处）**：
  - `App.StartMonitoring` → `StartMonitoringAsync`（fire-and-forget
    调用点改 `_ =`，方法体已有 try/catch 兜底）；
  - `App.RefreshTaskbarWidget`（定时器 Tick 链回调）→
    `RefreshTaskbarWidgetAsync` + 新增方法体 try/catch
    （原靠调用方 catch 无法捕获异步异常）。
- **事件处理器 try/catch 补齐（36 处）**：机械包裹整方法体
  （`catch (Exception ex) { Serilog.Log.Error(...) }`），涉及 15 个
  View 文件（AppStatsTab/NotesPage/CategoryManagementTab/
  WebsiteCategoryTab/DashboardPage/KeyboardDetailsTab/SankeyView/
  TimelinePage/WebAccessPage/WebHistorySection/WebStatsTab/
  DataManagementPage/QuickNoteWindow/AppDetailDialog 等）。
- **新增 `scripts/audit_async_void.ps1`**：async void 覆盖率核验脚本
  （61→58 处带 try/catch 覆盖率 100%，注释行已排除）。
- **验收实验**：在 `DashboardPage_Loaded` 临时注入
  `InvalidOperationException` 实测——应用**未崩溃**（进程存活）、
  日志出现 `[ERR] DashboardPage_Loaded 处理失败`+异常栈、0 FTL；
  实验代码已移除。
- **验证**：build 0 错误（24 警告=基线）；187/187；守卫 28/28；
  冒烟 PASS（3.82.0，0 ERR/FTL，主窗口正常）。

## [3.81.0] - 2026-11-06

### 变更：工程改造计划阶段 E3-8——DataManagement 收尾，E3 阶段完成

E3 第八批（收官）：数据管理页六分部去 `IDbContextFactory`。
**至此 E3 阶段（ViewModel 去 DbContext）全部完成**——所有 ViewModel
不再注入 `IDbContextFactory`。

- **新增 `PChabit.Application/Queries/DataManagementQueryService`**：
  `LoadOverviewStatsAsync` 收口概览统计（四表 Count + 最早/最新会话时间）；
  备份/清理/导出/云同步分部的 Services 接口依赖（IBackupService/
  IWebDAVSyncService/IExportService）保持原位不动。
- **白名单收缩 34→28**（主文件+五分部各清 1 条 Data using）；
  审计 `usingInfrastructureCount` 60→54。
- **E3 阶段总结**：8 批完成（3.74.0–3.81.0），14 个 VM 文件组全部
  服务化；白名单 50→28，剩余 28 条全部为 DTO record（KpiCard/
  HourHeatItem 等）与 Services 接口（IAnalyticsAiService 等）依赖，
  属下一阶段「DTO 归属迁移」范畴。
- **验证**：build 0 错误（24 警告=基线）；187/187；守卫 28/28；
  冒烟 PASS（3.81.0，0 ERR/FTL）；停实例→部署→恢复闭环
  （部署时撞上实例自动重启占用目录，二次停后部署成功——经验已记台账）。

## [3.80.0] - 2026-11-06

### 变更：工程改造计划阶段 E3-7——CategoryCrud 写操作服务化

E3 第七批（写操作模式）：应用/网站分类管理三文件去 `IDbContextFactory`。

- **新增 `PChabit.Application/UseCases/CategoryCrudService<TCategory,TMapping>`**：
  通用 CRUD 服务基类——模板方法与抽象钩子结构自原 VM 基类整体平移
  （读 LoadAsync + 增删改五方法），SaveChanges 时点与软删除语义
  （IsActive=false）逐字保持。
- **新增两个具体服务**：`ProgramCategoryCrudService`（进程名映射）、
  `WebsiteCategoryCrudService`（域名映射）——原两子类的 EF DbSet
  差异实现原样承接（Include 导航/过滤/排序/Find）。
- **VM 基类瘦身为 UI 编排**：`CategoryCrudViewModelBase` 不再持
  `DbFactory`，CRUD 转调注入的服务；ObservableCollection 填充/过滤/
  日志留 VM；两子类删全部 EF 钩子，只留搜索键差异与运行进程枚举。
- **白名单收缩 37→34**（三文件各清 1 条 Data using）；审计
  `usingInfrastructureCount` 63→60。
- **验证**：build 0 错误（24 警告=基线）；187/187；守卫 34/34；
  冒烟 PASS（3.80.0，0 ERR/FTL）；停实例→部署→恢复闭环。
  写链路为逐字平移（同 SaveChanges 模式），运行期建议用户打开
  「设置-应用分类/网站分类」页做一次增删验证。

## [3.79.0] - 2026-11-06

### 变更：工程改造计划阶段 E3-6——WebDetails 四分部查询服务化

E3 第六批：网页访问详情页（partial 四分部）去 `IDbContextFactory`。

- **新增 `PChabit.Application/Queries/WebDetailsQueryService`**：
  四类查询收口——`LoadCategoryNamesAsync`（启用分类）、
  `LoadAppHourlyAsync`（应用小时聚合，失败降级不影响网页侧）、
  `LoadSessionsAsync`（搜索/分类过滤含物化 CategoryName 回退逻辑）、
  `LoadBrowserUsageAsync`（浏览器流量归因行）。各降级语义逐字保留；
  浏览器进程名过滤留在 VM（依赖其 BrowserProcesses 常量）。
- **消费点适配**：`WebStatsTab.xaml.cs` 原手动 `new WebDetailsViewModel
  (dbFactory, ...)` 改为从 DI 取 `WebDetailsQueryService` 构造。
- **白名单收缩 41→37**（四分部各清 1 条 Data using）；审计
  `usingInfrastructureCount` 67→63。
- **验证**：build 0 错误（24 警告=基线）；187/187；守卫 37/37；
  冒烟 PASS（3.79.0，0 ERR/FTL，主窗口正常）；停实例→部署→恢复闭环。

## [3.78.0] - 2026-11-06

### 变更：工程改造计划阶段 E3-5——WebHistory 查询服务化

E3 第五批：网页浏览历史页去 `IDbContextFactory`。

- **新增 `PChabit.Application/Queries/WebHistoryQueryService`**：
  `LoadAsync` 返回域名聚合行（record 数/URL 数/最近访问）+ 每域明细
  索引；投影查询（AsNoTracking 最小字段）、`ExtractDomain` 口径
  （无 scheme 补 http、Host 小写归一，与前端 report.js 一致）逐字保留。
- **VM 侧**：百分比与排序（UI 投影）留在 VM；`_details` 明细索引由
  查询结果接管（原 LoadOnBackgroundAsync 内的聚合逻辑整体下沉）。
- **批次调整**：原计划本批同做的 CategoryCrud×3（应用/网站分类管理，
  CRUD 基类持 protected `DbFactory` + 6 处写操作 + 抽象钩子收
  `PChabicDbContext` 参数）属写操作模式，需重设计 CRUD 服务边界，
  移出本批单独处理（已登记台账与计划表）。
- **白名单收缩 42→41**。验证：build 0 错误（24 警告=基线）；187/187；
  守卫 41/41；审计 `usingInfrastructureCount` 68→67；冒烟 PASS
  （3.78.0，0 ERR/FTL）；停实例→部署→恢复闭环。

## [3.77.0] - 2026-11-06

### 变更：工程改造计划阶段 E3-4——AppDetail/DetailDialog 查询服务化

E3 第四批：两个详情弹窗 VM 去 `IDbContextFactory`。

- **新增 `PChabit.Application/Queries/AppDetailQueryService`**：
  `BuildReportAsync` 透传 `AppDetailEngine.BuildAsync`（原 VM 的
  `Task.Run` 后台执行语义收进服务，逻辑零变化）。说明查询与统计并行的
  结构（陷阱 13 风格）保持在 VM。
- **新增 `PChabit.Application/Queries/DetailDialogQueryService`**：
  `LoadKeyboardSessionsAsync`/`LoadMouseSessionsAsync`/`LoadWebSessionsAsync`
  三方法，各自的容错降级（空列表 + Log.Warning）逐字保留；按键排行/
  应用按键统计/每小时分布等聚合展示留在 VM。
- **白名单收缩 44→42**：两 VM 的 `using PChabit.Infrastructure.Data` 清除。
  残留 `Analysis`/`Services` using 为 DTO 与接口依赖（IWebsiteCategoryService/
  IAppDescriptionService），随后续批次处理。
- **验证**：build 0 错误（24 警告=基线）；187/187；分层守卫 42/42 无新
  违规；审计 `usingInfrastructureCount` 70→68（归因：两 VM 各删 1 条 Data
  using）；冒烟 PASS（3.77.0，0 ERR/FTL）；停实例→部署→恢复闭环。

## [3.76.0] - 2026-11-06

### 变更：工程改造计划阶段 E3-3——AppStats/KeyboardDetails 查询服务化

E3 第三批：计划优先级第 5 位的两个 VM 去 `IDbContextFactory`。

- **新增 `PChabit.Application/Queries/AppStatsQueryService`**：
  `BuildReportAsync` 透传 `AppStatsEngine.BuildAsync`（周期/分类过滤/搜索
  三参，逻辑零变化）。应用说明批量加载（`IAppDescriptionService`）仍在 VM
  后台阶段执行（其属 Services 接口，不在本批范畴）。
- **新增 `PChabit.Application/Queries/KeyboardDetailsQueryService`**：
  `LoadAsync` 返回当日 KeyboardSessions+MouseSessions 原始行（两表合一，
  加载日志保留在服务内）；键位聚合/快捷键 Top/每小时统计等计算留在 VM
  （消费其内嵌 stats 模型）。
- **白名单收缩 46→44**：两 VM 的 `using PChabit.Infrastructure.Data` 清除。
  AppStats 残留 `Analysis`/`Services` using 为 DTO 与接口依赖，随后续批次
  处理；KeyboardDetails 已无任何 Infrastructure using。
- **验证**：build 0 错误（24 警告=基线）；187/187；分层守卫 44/44 无新
  违规；审计 `usingInfrastructureCount` 72→70（归因：两 VM 各删 1 条 Data
  using）；冒烟 PASS（3.76.0，0 ERR/FTL）；常驻实例停→部署→恢复闭环。

## [3.75.0] - 2026-11-06

### 变更：工程改造计划阶段 E3-2——Timeline/NetworkTraffic 查询服务化

E3 第二批：计划优先级 3/5 位的两个 VM 去 `IDbContextFactory`。

- **新增 `PChabit.Application/Queries/TimelineQueryService`**：
  `LoadDayAsync`（当日 AppSessions+WebSessions+分类映射三合一，查询失败
  降级空列表的容错行为逐字保留）、`LoadHourAsync`（展开时段按需查询，
  原两段 `Task.Run` 内建库的写法收进服务）。时间线页主查询与时段展开
  均不再接触 factory；`TimelineStats`/`TimelineHourGroup` 等 UI 绑定模型
  留在 VM（投影不迁）。
- **新增 `PChabit.Application/Queries/NetworkTrafficQueryService`**：
  `LoadAsync` 返回分钟样本+进程小时行+分类映射；映射加载失败降级空映射
  （行为不变）。`BuildDayBars/ProcessRows/CategoryRows/HourBars` 四个 UI
  投影方法留在 VM；`_categoryMap` 字段类型放宽为 `IReadOnlyDictionary`。
- **DI**：两服务经 `AddTaiApplication` 注册；VM 构造签名由 DI 自动适配。
- **白名单收缩 48→46**：两 VM 的 `using PChabit.Infrastructure.Data` 清除。
  残留 `Analysis` using 为 `AppStatsEngine` 静态与 `IsProductiveCategory`
  依赖，随 DTO 批次处理。
- **验证**：build 0 错误（24 警告=基线）；187/187；分层守卫 46/46 无新
  违规；审计 `usingInfrastructureCount` 74→72（归因：两 VM 各删 1 条 Data
  using）；部署时发现用户常驻实例占用部署目录（既有流程：停实例→部署
  →冒烟→恢复）；冒烟 PASS（3.75.0，0 ERR/FTL）；常驻实例已恢复运行。

## [3.74.0] - 2026-11-06

### 变更：工程改造计划阶段 E3-1——Analytics/Sankey 查询服务化（VM 去 DbContext 首批）

**目标**（计划阶段 E3）：ViewModel 不再注入 `IDbContextFactory`，改为
注入查询服务。本批覆盖计划优先级第 2/3 位的两个 VM。

- **新增 `PChabit.Application/Queries/AnalyticsQueryService`**：
  `BuildReportAsync(period)` 透传 `AnalyticsEngine.BuildAsync`，
  `AnalyticsViewModel` 不再持 `_dbContextFactory`，报告构建经服务产出
  （计算逻辑零变化）。
- **新增 `PChabit.Application/Queries/SankeyQueryService`**：
  `LoadAsync` 返回 `SankeyQueryResult`（聚合数据 + 分类映射 + 会话快照
  分类），「分类映射 + 快照映射 + 聚合」三步合一收进服务，保持 3.45.0
  以来的统一分类口径（陷阱 43：漏快照映射会出现「未分类」假象）。
  `SankeyViewModel.ToJson` 的分类解析改用服务返回的映射，行为不变。
- **DI**：两个查询服务经 `AddTaiApplication` 注册；VM 构造签名变化由
  DI 自动适配（`AnalyticsPage`/`SankeyView` 均 `GetRequiredService` 取 VM）。
- **白名单收缩 50→48**：两 VM 的 `using PChabit.Infrastructure.Data` 清除
  （DbContext 依赖归零）。残留的 `Infrastructure.Analysis` using 为 DTO
  record（KpiCard/HourHeatItem/SankeyData 分类辅助等）与 `AppStatsEngine`
  静态依赖，DTO 归属迁移留独立批次，同 commit 登记白名单。
- **验证**：build 0 错误（24 警告=基线）；187/187；分层守卫 0 新违规、
  0 过期条目；审计 `usingInfrastructureCount` 76→74（归因：两 VM 各删
  1 条 Data using）；冒烟 PASS×2（第二次主窗口正常检出；首次 PASS 带
  30s 边界窗口检测 WARN，为检测时序抖动，非启动异常）。

## [3.73.0] - 2026-11-06

### 变更：工程改造计划阶段 E2-b/E2-c——DataCollectionService 整类迁入 Application 层

`src\PChabit.App\Services\DataCollectionService.*`（6 个分部文件，约 2,243 行）
按计划 E2-b/E2-c 批次整类迁入 `PChabit.Application\UseCases\Collection\`。
用例编排自此不再住在表示层。**E2 物理迁移全部完成**（E2-a 接口下沉 3.72.0
+ 本批）。

- **跨程序集迁移必须整类搬运**：partial 类不能拆散在两个程序集，6 个分部
  文件（.Analysis/.Collection/.Maintenance/.Persistence/.Web/主文件）一次
  全部搬移，仅改命名空间（`PChabit.App.Services` →
  `PChabit.Application.UseCases.Collection`），逻辑零变化。
- **新增 `PChabit.Core/Interfaces/IDataCollectionService.cs`**：App 层消费方
  （App.xaml.cs 启动/关机、App.Startup、App.DesktopWidget）不再引用具体类，
  只依赖 Core 接口（`Start`/`Stop`/`CurrentProcessName`）。
- **DI 注册同批收口**：`AddSingleton<接口>(实现)` 注册对齐 E2-a 适配器模式
  （陷阱 3：注册与实现同批提交）。
- **验证**：build 0 错误（24 警告=基线）；`dotnet test` **187/187 通过**；
  部署 `D:\Tool\PChabit` 自包含校验通过（runtimeconfig includedFrameworks、
  exe 3.73.0.0 与 csproj 一致）；启动 30s **0 ERR/0 FTL**，进程存活、主窗口
  正常；冒烟 **PASS**。

## [3.72.0] - 2026-11-06

### 变更：工程改造计划阶段 E2-a（修订版）——硬件/网络数据源接口下沉 Core

用户拍板「接口下沉 Core」方案。两持久化服务不再直接依赖平台 TFM 的
HardwareMonitor 具体类型，改为依赖 Core 纯数据契约；HardwareMonitor 由
App 侧适配器桥接。**物理迁移 Infrastructure 的技术阻塞已解除**（留待后续
独立批次执行）。

- **新增 `PChabit.Core/Interfaces/IHardwareTelemetry.cs`**（零第三方/WinRT 依赖）：
  `IHardwareTelemetrySource`（`ValuesUpdated` 事件 + `Get(key)` 传感器查询）、
  `IProcessNetworkSource`（系统今日累计 / 全部进程今日累计 / 速率 TopN 快照 /
  双维度种子回填）、载荷类型 `NetworkProcessTotals` / `NetworkRateItem`
  （消费面按两服务实测 API 逐一核定，最小面设计）。
- **新增 `App/Services/HardwareTelemetryAdapters.cs`**：
  `HardwareTelemetrySourceAdapter`（事件 +=/-= 直通转发）与
  `ProcessNetworkSourceAdapter`（`ProcessNetworkItem` → Core 载荷映射）。
- **两服务改依赖接口**（逻辑零变化，仅类型替换）：
  `HardwareSampleWriter` 注入 `IHardwareTelemetrySource`；
  `NetworkTrafficPersistenceService` 注入 `IProcessNetworkSource`，
  `FlushNow` 的 `GetSnapshot(3).TopByRateList` 改为 `GetTopRates(3)`，
  `WriteProcessHourDeltas` 参数类型 `List<ProcessNetworkItem>` → `List<NetworkProcessTotals>`。
- **DI**（陷阱 3：注册与实现同批）：适配器单例 + 接口→适配器转发注册；
  两服务构造签名不变位置不变，`App.Startup`/`App.Shutdown` 零改动。
- 新增 5 个 Core 接口契约测试（`HardwareTelemetryContractTests`），测试 182 → **187**。
  说明：适配器与两服务在 App 层（平台 TFM），Tests（net10.0）无法引用——
  装配正确性由运行期实证覆盖（该限制与 E2-a 原阻塞同根源）。

**验证**：构建 0 错误（24 警告 = 基线）；187/187 测试；发布 3.72.0.0 部署
（15 文件）；自包含校验通过。运行期实证：启动后 `HardwareSampleWriter`/
`网络流量落库服务` 均正常启动、0 ERR/0 FTL；**实数据落点**——
`HardwareSamples` 与 `NetworkTrafficSamples` 启动后每分钟 1 行持续写入
（08:02–08:09 共 8 行），`ProcessNetworkUsages` 当日 333 行，
采集→适配器→接口→服务→落库全链路无回归；冒烟 PASS。

## [工程改造计划 阶段 E1] - 2026-11-06

### 变更：应用层边界定义 + 分层守卫脚本（阶段 E1，无独立版本号，沿用阶段 A 先例）

阶段 E1 完整落地（纯 CI/脚本/文档，未动应用代码，故沿用阶段 A 先例不递增版本号——
当前版本仍为 3.71.0）。

- **新增 `scripts/check_layering.ps1` 分层守卫**：扫描
  `src/PChabit.App/ViewModels/**` 的 `using PChabit.Infrastructure.*`，
  与 `scripts/layering-allowlist.txt` 基线白名单比对——**新增违规退出码 1（CI 阻断）**，
  移除仅黄字提示收缩白名单。三态自测通过：当前码 0 → 注入违规行 1 → 还原 0。
- **生成基线白名单（50 条）**：计划文档「62 条」为含 App 层的旧估数，实测 ViewModels
  为 50 条，按实测生成（生成命令见 `docs/项目文档.md` 3.72.0 节）。白名单只减不增，
  E3 每完成一个 VM 查询服务化即删除对应条目。
- **接入 CI**（`ci.yml` test job，阻断项）：`test` → `layering guard` → 均必过。
- **「分层边界规约」落档 `AI_MAINTENANCE.md`**：四层依赖表（Core/Application/
  Infrastructure/App 各自允许与禁止）+ 4 条硬性规则（VM 新增 Infrastructure using
  一律不允许、白名单只减不增等）。
- **E2-a 阻塞记录**（AI_MAINTENANCE.md 同节）：`HardwareSampleWriter` /
  `NetworkTrafficPersistenceService` 无法迁 Infrastructure——两者依赖
  `PChabit.HardwareMonitor`（平台 TFM `net10.0-windows10.0.22621.0`），而
  Infrastructure 为基础 TFM `net10.0`，基础 TFM 不能引用平台 TFM。修订建议三选一
  （接口下沉 Core / HardwareMonitor 拆双 TFM / Infrastructure 改平台 TFM）留待
  后续批次决策；E2-b/E2-c（DataCollectionService，仅依赖 EF/Core）不受影响。

**验证**：分层守卫当前码退出码 0（50 found / 50 allowlisted / 0 violations）；
注入 `using PChabit.Infrastructure.Data;` 后退出码 1 并红字列出违规条目；还原后 0。
`audit_code.ps1` 基线复核正常（silent catch 6 / catch(Exception) 398 / using Infra(VM) 89）。

## [3.71.0] - 2026-11-06

### 变更：工程改造计划阶段 D4（步骤 2b+3，收尾）——仪表盘查询链下沉应用层

`DashboardViewModel` 1053 → **703** 行；新增
`PChabit.Application/Queries/DashboardQueryService.cs`（380 行，原样搬运）。
**至此 D4 四步全部完成，仪表盘形成「查询服务 → 快照 → 绑定」三层结构。**

- 查询链整体迁移（`LoadStatsOnBackgroundAsync` 及其全部私有依赖）：
  `BuildStatsFromRawDataAsync` / `FillCompareAsync` / `SafeScoreAsync` /
  `SafeDeviationAsync` / `LoadCategoryMapAsync` → `DashboardQueryService.LoadAsync`，
  行为逐字一致（今日实时合并不写库、效率/基线/环比各自容错降级）。
- VM 构造函数不再注入 `IDbContextFactory` 与 `IEfficiencyCalculator`，
  改注入 `DashboardQueryService`；`LoadStatsOnBackgroundAsync` 只剩一行委托调用，
  VM 仅保留「快照 → 绑定属性」映射 + 图标加载 + 文本格式化（计划既定职责）。
- DI 注册收口在 `ServiceCollectionExtensions.AddTaiApplication`（陷阱 3 对策：
  注册与实现同批提交）；`PChabit.Application` 补 `Serilog` 包引用（与
  Infrastructure 同版本策略 4.*）。
- 新增 8 个单测（`DashboardQueryServiceTests`，内存 SQLite + Moq）：空库安全默认、
  聚合缺失实时补齐（不写库断言）、效率计算抛异常降级 0、成功值透传、
  未结束会话→当前活动、昨日环比（含截断点口径）、昨日无数据→环比 null、
  Legacy Web 会话排除。测试 **174 → 182**。

**验证**：构建 0 错误（24 警告 = 基线）；182/182 测试；发布 3.71.0.0 部署
（15 文件更新）；自包含校验通过（`includedFrameworks`/5 关键 DLL/13 Assets png）；
运行期实证：启动 30s+ 日志 0 ERR/0 FTL（3 条 WRN 为既有已知 OpenRouter 404 白名单项），
仪表盘数据加载与旧版口径逐项一致（AppSessions=577 / KeyboardSessions=8 / 2284 键 /
MetricsVersion=2），30 秒实时刷新精确循环、`Interlocked` 防重入未受影响，
AI 降级路径正常（保留规则摘要）。

## [3.70.0] - 2026-11-06

### 变更：工程改造计划阶段 D4（步骤 2a）——仪表盘数据快照迁往应用层

`DashboardViewModel` 1123 → **1053** 行；新增
`PChabit.Application/Queries/DashboardSnapshot.cs`（73 行）。

- 把 VM 内的**数据快照类型群**（原嵌套类）整体迁往应用层并改名：
  `DashboardStats` → `DashboardSnapshot`，连带其字段依赖的 4 个纯 POCO
  （`HourlyActivityData` / `WebsiteUsageData` / `ActivityBar` / `ChartSnapshot`）。
- VM 侧：删除该类型群（70 行）+ 新增 `using PChabit.Application.Queries;` +
  8 处引用改名（含 1 处全限定 `DashboardViewModel.DashboardStats`——
  迁出嵌套后全限定路径失效，正是本次编译先报 `CS0146 循环基类型依赖`
  的原因，去掉限定前缀即修复）。
- 零行为变化（纯类型搬迁 + 改名）。

**验证**：构建 0 错误（11 警告）；测试 **174/174**；发布 3.70.0.0 部署；
运行期仪表盘卡片正常、关键指标文本存在、新增 ERR/FTL=0、WRN=0。

**遗留（D4 步骤 2b–3）**：`BuildStatsFromRawDataAsync`（229 行，含 EF 查库与
聚合）拆出 `Application/Queries/DashboardQueryService`，VM 只保留
「快照 → 绑定属性」映射；保持「今日数据实时合并、不写库」与 `Interlocked`
刷新防重入（陷阱 27）不变。

## [3.69.0] - 2026-11-06

### 变更：工程改造计划阶段 D4（步骤 1）——提取仪表盘纯计算 + 单测

按计划分步推进（步骤 1 = 先搬纯计算并补单测，风险最低）：
`DashboardViewModel` 1163 → 1123 行；新增
`PChabit.Infrastructure/Analysis/DashboardMetricsCalculator.cs`（静态纯函数，零行为变化）。

- 提取 5 个纯计算（逐字搬运，仅可见性 `private`→`public`）：
  `ComparePct`（环比，基数为 0 返回 null）、`ParseNumber`（千分位文本取数字）、
  `SessionActiveMinutes`（会话有效时长）、`SessionEnd`（会话结束时刻）、
  `FormatDuration`（时长格式化）。
- 6 处调用点改为 `DashboardMetricsCalculator.X(...)`（`FillCompareAsync` /
  `BuildStatsFromRawDataAsync` / 网站时长映射）。

**验证**：
- **新增 18 个单测**（`DashboardMetricsCalculatorTests`，计划要求 ≥6），覆盖
  环比基数为 0/负数/正负增减与四舍五入、文本解析（千分位 / 中文单位 / 空）、
  会话时长三情形（有 `ActiveDuration` / 已结束回退差值 / 历史未闭合计 0）、
  会话结束三情形、时长格式化边界（null / <1h / ≥1h）。
- 测试 **174/174 全过**（156 → 174）；构建 0 错误（13 警告）。
- 运行期（`scripts/verify_369.py`）：仪表盘卡片正常加载、关键指标文本存在、
  新增 ERR/FTL=0、WRN=0；发布 3.69.0.0 部署。

**遗留（D4 步骤 2–3）**：`BuildStatsFromRawDataAsync`（229 行，含查库）拆出
`DashboardQueryService`（返回 `DashboardSnapshot` DTO），VM 只保留「快照 →
绑定属性」映射；保持「今日数据实时合并、不写库」约束（陷阱 27 的 `Interlocked`
刷新保护不动）。

## [3.68.0] - 2026-11-06

### 变更：工程改造计划阶段 D3 完成——SettingsPage 全部分区拆为 UserControl

**`SettingsPage` 由 1921 行（1058 XAML + 919 cs）降至 200 行（97 + 103）**，
9 个分区卡片全部拆为 `Views/Settings/*Section` 自治 UserControl：

| 分区（UserControl） | 版本 | 备注 |
|---|---|---|
| AboutSettingsSection | 3.64.0 | 纯搬运 |
| AiSettingsSection | 3.65.0 | 首次重构 |
| StickyNotesSection | 3.66.0 | 热键校验 + WebDAV 同步 |
| GeneralSettingsSection / MonitorSettingsSection | 3.67.0 | — |
| TaskbarSettingsSection / DesktopWidgetSettingsSection / AppearanceSettingsSection | 3.68.0 | — |
| DataSettingsSection | 3.68.0 | **x:Bind → 自治模式**（见下） |

- **DataSettingsSection 顺带修正一处隐患**：原「数据」卡片用
  `{x:Bind ViewModel.X, Mode=TwoWay}` 直接绑页面 ViewModel，改动只写 ViewModel、
  **不调用 `SaveSetting`**（不落盘）。拆分时统一为与其它分区一致的
  `RaiseChanged` + `SaveSetting`，使设置项改动真正持久化。
- **页面收尾清理**：所有分区迁出后，`AddEventHandlers` / `OnSettingChanged` /
  `LoadSettingsToUI` 成为空壳（含 `_isLoading`），一并删除；页面只保留分区导航
  与「注入 `Initialize(vm)` / 刷新 `Reload()`」编排。警告维持基线 24。
- 页面 using 精简为 5 条（UI / Navigation / Serilog / ViewModels）。

**验证**（新增 `scripts/verify_368.py`）：
- **7 个导航分区逐个切换全部成功**，右侧内容正确（陷阱 31 回归无异常）。
- **数据分区读写实证**：`DataRetentionBox` 120 → 110 → 日志
  `设置变更 DataRetentionDays = 110` → `SaveSetting` → `settings.json` 落盘。
- 构造函数完成；新增 ERR/FTL=0；构建 0 错误（24 警告=基线）；156/156 测试。
- 发布 3.68.0.0 部署 `D:\Tool\PChabit`。
- *冒烟说明*：`smoke_test.ps1` 报 FAIL 系**单实例保护**（用户实例正在运行，
  新实例按设计 `Exit(0)`，异步日志未及 flush）——非代码缺陷；等效验证由
  `verify_368.py`（先清理进程再启动）完成并通过。

**D3 收官**：9/9 分区拆分完成，SettingsPage 宿主化（200 行）。

## [3.67.0] - 2026-11-06

### 变更：工程改造计划阶段 D3（步骤 3a）——「常规」「监控」分区拆为 UserControl

`SettingsPage.xaml` 509 → 388 行、`SettingsPage.xaml.cs` 344 → 296 行；新增
`Views/Settings/GeneralSettingsSection.xaml(.cs)`（64 / 80 行）与
`MonitorSettingsSection.xaml(.cs)`（79 / 87 行）——**至此页面从 1921 行降至 684 行**。

- 两个分区按既有自治模式拆分（注入同一 `SettingsViewModel`；
  `RaiseChanged` / `LoadFromViewModel`）。
- 常规：4 个开关（开机自启 / 最小化到托盘 / 显示通知 / 自动开始监控）。
- 监控：2 个 NumberBox（监控间隔 / 空闲阈值）+ 3 个开关（键盘 / 鼠标 / 网页活动）。

**验证**（新增 `scripts/verify_367.py`）：
- **常规分区**：切换「显示通知」→ 日志 `设置变更 ShowNotifications = true` →
  `settings.json` 由 `false` 变 `true`。
- **监控分区**：`MonitoringIntervalBox` 由 1 改为 3 → 日志 `设置变更 MonitoringInterval = 3`
  → `settings.json` 同步为 `3`。
  （UIA 树中该分区 ToggleSwitch 未暴露 AutomationId，改以 NumberBox 的 RangeValuePattern
  验证读写，同样覆盖 Section 的 `RaiseChanged`/`LoadFromViewModel` 链路。）
- 新增 ERR/FTL=0；构建 0 错误（24 警告=基线）；156/156 测试；发布 3.67.0.0 部署、冒烟 PASS。

**遗留（D3 步骤 3b）**：外观 / 任务栏 / 桌面插件 / 数据 四个分区（对应「常规」分组的外观卡、
「界面」分组的两卡、「数据」分组）。

## [3.66.0] - 2026-11-06

### 变更：工程改造计划阶段 D3（步骤 2b）——SettingsPage「便签」分区拆为 UserControl

`SettingsPage.xaml` 647 → 508 行、`SettingsPage.xaml.cs` 469 → 340 行；
新增 `Views/Settings/StickyNotesSection.xaml(.cs)`（150 / 192 行）。

- 沿用 AI 分区（3.65.0）的自治模式：便签段 4 个 case + Load 段 7 行迁入分区，
  改造为 `RaiseChanged()` / `LoadFromViewModel()`（注入同一 `SettingsViewModel`）。
- `StickyHotkeyBox_LostFocus`（热键解析 + 真实注册探测）、`StickySyncNow_Click`
  （WebDAV 立即同步）及 4 条事件绑定一并搬入。
- **踩到陷阱 29 的新变体**：Section 类名 `StickyNotesSection` 与搬入的
  内部根 Border `x:Name="StickyNotesSection"` **同名** → `CS0542 成员名不能与封闭类型相同`
  （XAML 生成的字段与类同名）。修复：去掉内部 Border 的 `x:Name`（宿主改用
  `StickyNotesControl` 引用该 UserControl）。**后续分区注意：UserControl 类名不得
  与其内部任何 `x:Name` 相同。**
- 顺带消除本次引入的 3 个 `CS8602`（在事件处理器首部补 `_viewModel is null` 守卫），
  警告维持 13（低于基线 24）。

**验证**（新增 `scripts/verify_366.py`）：
- 设置页打开不崩溃；便签分区选中；内容（便签 / 热键 / 同步 / 保留）加载正常。
- **读写链路实测**：切换「聚焦丢失时快速隐藏」→ 日志
  `设置变更 StickyNotesQuickDismissOnFocusLost = false` → **`settings.json` 该键
  由 `true` 变为 `false`**；新增 ERR/FTL=0。
- 构建 0 错误；156/156 测试；发布 3.66.0.0 部署、冒烟 PASS。

**遗留（D3 步骤 3）**：常规 / 监控 / 界面（任务栏+桌面插件+外观三卡）/ 数据 四个
分区，按同模式拆分。

## [3.65.0] - 2026-11-06

### 变更：工程改造计划阶段 D3（步骤 2a）——SettingsPage「AI」分区拆为 UserControl

`SettingsPage.xaml` 802 → 646 行、`SettingsPage.xaml.cs` 617 → 468 行；
新增 `Views/Settings/AiSettingsSection.xaml(.cs)`（165 / 211 行）。

**这是首次「重构」而非纯搬运**——AI 分区原本与页面**集中式**读写耦合
（`LoadSettingsToUI` / `OnSettingChanged` 按 `x:Name` 统一遍历）：
- 把 `OnSettingChanged` 的 AI 分支（16 个 case）与 `LoadSettingsToUI` 的 AI 段
  （17 行）**移入分区**，改造为分区自治的 `RaiseChanged()` / `LoadFromViewModel()`。
- 分区持有注入的 `SettingsViewModel`（宿主构造期传入**同一实例**），
  事件写回 ViewModel 并 `SaveSetting`——语义等价原页面集中式逻辑。
- 分区的 `_isLoading` 保护随逻辑一起迁移（`LoadFromViewModel` 内自包裹，
  等价原页面外层保护），继续抑制 `RadioButton.IsChecked` 赋值触发的事件（陷阱 31 类）。
- `ApplyEndpointModeHint` / `AiCopyTestResult_Click` / `AiTestConnection_Click`
  及 14 条事件绑定一并搬入分区（`OnSettingChanged(`→`RaiseChanged(`、
  `ViewModel.`→`_viewModel.` 为必要的标识符改写）。

**验证**（新增 `scripts/verify_365.py`，uiautomation）：
- 设置页打开不崩溃；AI 分区可选中；内容（端点 / 模型 / 超时）加载正常。
- **读写链路实证**：切换「每周自动 AI 解读」→ 日志 `设置变更 AiAutoWeeklyInsight = true`
  → **`settings.json` 中该键由缺省变为 `true`**（自治保存生效）；新增 ERR/FTL=0。
- 构建 0 错误（12 警告）；156/156 测试；发布 3.65.0.0 部署、冒烟 PASS。

**过程教训（陷阱 52 重演）**：改版本号时用 PowerShell `Set-Content -NoNewline` 写
`PChabit.App.csproj`（看似纯 ASCII、实际含中文）再次触发 GBK↔UTF-8 污染，
`git checkout` 回滚后改用结构化编辑工具。AI_MAINTENANCE 陷阱 52 已升级结论：
**禁止用 PowerShell 文本管道写任何项目文件，无论其看起来是否含非 ASCII**。

**遗留（D3 步骤 2b–3）**：便签 / 常规 / 监控 / 界面 / 数据 五个分区，同样需按
本条模式把集中式 Load/Save 片段迁入各自 UserControl。

## [3.64.0] - 2026-11-06

### 变更：工程改造计划阶段 D3（步骤 1）——SettingsPage「关于」分区拆为 UserControl

按计划分步推进（步骤 1 = 最独立的「关于」分区，验证 Page→UserControl 模式）：
`SettingsPage.xaml` 1058 → 802 行、`SettingsPage.xaml.cs` 919 → 616 行；
新增 `Views/Settings/AboutSettingsSection.xaml(.cs)`（265/319 行）。

- **拆出内容**：`LoadAboutInfo` / `ResolvePackageVersion` / `NormalizeVersion` /
  `LoadLatestChangelogSummary` / `OnViewChangelogClick`（关于页展示 + 更新日志弹窗）
  及 `_fullChangelog` 字段——均为只读展示、不参与页面集中式设置读写，故为**纯搬运**。
- **接口**：`AboutSettingsSection.Refresh()`（供宿主在构造与 `OnNavigatedTo` 调用，
  等价原 `LoadAboutInfo()` 两个调用点）；事件绑定（`ViewChangelogButton.Click`）
  移入分区自身构造函数。
- **四处同步改**（陷阱 29）：根节点 `Page`→`UserControl`、`x:Class` 改为
  `PChabit.App.Views.Settings.AboutSettingsSection`、文件内无 `Resources`、
  结束标签 `</Page>`→`</UserControl>`。
- **页面侧**：新增 `xmlns:settings`，原 `AboutSection` Border 替换为
  `<settings:AboutSettingsSection x:Name="AboutSectionControl" />`；
  `ApplySection` 改引用 `AboutSectionControl`；两处 `LoadAboutInfo()` 调用改为
  `AboutSectionControl.Refresh()`。
- 字节保真脚本（22 个边界锚点断言 + 行区间删除/替换），XAML 缩进原样保留。

**验证**（新增 `scripts/verify_364.py`，uiautomation）：
- **陷阱 31 回归通过**：进入设置页不崩溃，日志「SettingsPage: 构造函数完成」，新增 ERR/FTL=0。
- **7 个分区逐个选中（SelectionItemPattern）全部成功**，右侧正确切换。
- **「关于」内容全部加载**：可见文本含 文件版本 / WinUI 3 / 技术栈 / 更新日志 /
  「最新更新 · 3.64.0」（CHANGELOG 摘要实时读取成功）——证明 UserControl 内
  `x:Name` 控件正确绑定。
- 构建 0 错误；156/156 测试；发布 3.64.0.0 部署、冒烟 PASS。
- *验证脚本踩点*：设置页分区项与主导航存在**同名项**（如「便签」），按名称遍历会
  误选主导航项；已加坐标过滤（x>300）修正，供后续分区拆分复用。

**遗留（D3 后续步骤 2–3）**：AI / 便签 / 常规 / 监控 / 界面 / 数据 六个分区与页面
**集中式**设置读写强耦合（`LoadSettingsToUI` 92 行 + `OnSettingChanged` 189 行按 `x:Name`
统一遍历所有分区的控件），拆分它们需把集中逻辑**按分区打散到各分区的 Load/Save**——
属逻辑重构而非纯搬运，风险显著更高，留作后续批次逐分区进行（每批独立编译 + UI 验证）。

## [3.63.0] - 2026-11-06

### 变更：工程改造计划阶段 D2——按职责拆分 App.xaml.cs（纯搬运）

1170 行单文件 → 6 个同 `partial class` 分部文件（含原有 `App.DesktopWidget.cs`/
`App.StickyNotes.cs`），**成员签名零变化**（38 个成员逐一比对一致）、**行为零变化**：

| 文件 | 行数 | 职责 |
|---|---|---|
| `App.xaml.cs` | 419 | 生命周期入口：字段 + 构造 + 异常钩子 + DI/日志配置 + `OnLaunched`/`OnWindowFirstActivated`/`OnWindowClosed`/`OnNavigationFailed` |
| `App.Window.cs` | 139 | **全部 P/Invoke 声明集中于此** + `SetWindowIcon` + `CorrectWindowBoundsIfOffscreen` + `FindMainWindowHandle` |
| `App.Tray.cs` | 32 | `InitializeTrayService`（托盘初始化 + ExitRequested 接线） |
| `App.Startup.cs` | 126 | `StartMonitoring`（钩子线程/采集/硬件/流量/落库链路）+ `StartBackupService` |
| `App.Taskbar.cs` | 284 | 任务栏小窗：定时器/刷新/`BuildTaskbarRows`/格式与配色辅助/今日时长与下载量查询 |
| `App.Shutdown.cs` | 218 | `BeginShutdown` + `PerformShutdownAsync`（5 步关闭）+ `ForceTerminate` |

**方法与过程**：
- 字节保真切割脚本：**46 个边界锚点断言** + 非空行内容守恒校验（仅新增 using 与
  骨架行为预期差异），LF 行尾原样保留（App.xaml.cs 为 LF，与 SettingsService 的
  CRLF 不同——脚本按实测换行处理）。
- **P/Invoke 集中**：31-70 行的 8 个 `DllImport` + `RECT` + `EnumWindowsProc` +
  图标常量整块移到 `App.Window.cs`；因同属一个 partial class，其它分部文件原有
  调用点**零改动**。
- 字段与构造函数保持集中在主文件，避免跨文件字段初始化顺序问题。
- 首轮编译报 1 个 C# 缺 using（`NetworkTrafficPersistenceService`）+ 1 个 `WMC9999`
  XAML 假错误——**再次验证陷阱 21：先修 C#，XAML 报错自动消失**。

**验证**：
- Release 构建 0 错误（11 警告，低于基线 24）；单测 156/156；成员签名 diff 为空（38 项）。
- **5 个分部文件的运行期实证**（用户真实环境日志）：`系统托盘服务初始化完成`、
  `窗口图标设置成功`、`监控器已启动`、`任务栏/托盘显示定时器已启动`、
  `TaskbarWidget 已创建并挂载任务栏`、`数据库初始化完成`、定时备份启动——全链路正常，
  本轮启动段无新增 ERR/FTL。
- publish 3.63.0.0 部署 `D:\Tool\PChabit`；冒烟 PASS（版本匹配/存活 30s/主窗口/0 ERR/FTL）。

**未实点**：窗口关闭最小化到托盘、托盘菜单退出流程（需 UI 交互；沙箱自动化环境下
延迟初始化不执行，`_trayService` 未就绪会干扰验证）。退出链路代码纯搬运零改动，
且 `PerformShutdownAsync` 各步骤在 3.61.0 批次已逐处补过日志。

## [3.62.0] - 2026-11-06

### 变更：工程改造计划阶段 D1——按职责拆分 SettingsService（纯搬运）

1470 行单文件 → 5 个同 `partial class` 分部文件，**公开 API 零变化**
（83 个签名逐一比对完全一致），**行为零变化**（无一行逻辑改动）：

| 文件 | 行数 | 职责 |
|---|---|---|
| `SettingsService.cs` | 527 | 字段 + 通用属性（系统/监控/跟踪/隐私/WebDAV/浏览器/任务栏/备份） + 事件 + 构造函数 |
| `SettingsService.Persistence.cs` | 215 | `Load/LoadAsync/Save/SaveAsync/ResetToDefaults/ApplySettings` + `AppSettings` DTO |
| `SettingsService.Startup.cs` | 207 | 开机自启（注册表 Run 键 / 启动文件夹快捷方式 / COM 校验与创建） |
| `SettingsService.Ai.cs` | 217 | AI 端点/模型/隐私/周报相关属性（16 个） |
| `SettingsService.Widgets.cs` | 327 | 桌面悬浮插件（17）+ 便签（7）相关属性 |

**方法与过程**：
- 用一次性 Python 脚本做**字节保真搬运**：27 个边界锚点断言 + 非空行内容守恒校验
  （仅 class 声明改写为 `partial class` 为预期差异），CRLF 行尾原样保留。
- 调用链核查：`Save → ApplySettings → ApplyStartupSetting` 依赖的 `_settings`/
  `_settingsPath` 字段与构造函数同在主文件，**无跨文件静态字段、无隐式初始化顺序依赖**。
- 计划中的 `Validation.cs`（默认值/范围校验/字符串规范化）**暂未创建**：当前代码
  仅 `AiTimeoutSeconds` 内联 `Math.Clamp`，无独立校验逻辑；默认值在 `AppSettings`
  DTO 初始化器内。避免为空文件而拆，待出现独立校验逻辑时再建。

**验证**：
- Release 构建 0 错误（24 警告=基线）；单测 156/156。
- **公开 API 签名对比**：拆分前后 83 个成员签名 diff 为空。
- **运行期 load 双分支实证**：无 settings.json → 日志「设置文件不存在，使用默认: …
  settings.json」（默认分支）；人为构造含非默认值的 settings.json → 日志「设置已加载: …
  settings.json」（反序列化分支），两次均 0 ERR/FTL。
- publish 3.62.0.0 部署 `D:\Tool\PChabit`；冒烟 PASS（版本匹配 / 存活 30s /
  主窗口正常 / 0 ERR/FTL）。

**未做**：设置页 7 分区 UI 逐一点开（需 uiautomation，纯搬运+编译期接口检查+
运行期 load 实证已覆盖；如需可后续补 `scripts/smoke_ui.py`）。

## [3.61.0] - 2026-11-06

### 变更：工程改造计划阶段 C——异常收口第二批 + 日志规范

承接 3.60.0（阶段 B）。本批仍为**可观测性补丁**：所有 catch 控制流零变化。

- **新增「无日志 catch」审计维度**：`audit_code.ps1` 升级——新增 silent catch
  扫描（括号深度追踪 catch 块体，判定 `Log.` / `throw` / `Debug.WriteLine` /
  `TrySetException`），输出位置清单。基线扫描：**74 处无日志 catch**。
- **无日志 catch 74 → 5**：剩余 5 处全部为登记豁免（取消模式 ×4、
  Core 零依赖豁免 ×1）。重点处理：
  - **查询类吞错升 `Log.Error`**（"吞错导致错误数据"模式，3.46.0 陷阱 45 案发地）：
    `AppDetailEngine` 键鼠/鼠标/硬件样本 3 处——查询失败时 UI 显示 0 属真实缺陷
    伪装，必须可观测；`AnalyticsEngine` 3 处查询降级升 `Warning`
  - **数据入库链路**：`BrowserSyncWebSocketHandler` 非法 JSON 静默丢弃升
    `Warning`（3.38 静默停更教训的入口变体）；`NetworkTrafficPersistenceService`
    日摘要落库失败升 `Warning`
  - **功能停用决策升 `Warning`**：`ProcessResourceMonitor` GPU 采样停用 ×2、
    `PerformanceCounterManager` 初始化失败（回退 LHM）
  - **钩子防线**（键盘/鼠标钩子回调内异常必须吞，否则 Windows 静默卸载钩子）：
    保持吞但补 `Log.Debug`，登记为规约豁免点
  - 其余 50+ 处探测/竞态/关闭期降级按 C2 规范补 `Log.Debug` + 注释
- **empty catch 13 → 10**（达标 ≤10），每处均有注释说明
- **C2 日志级别规范**写入 `AI_MAINTENANCE.md`：Debug（可自愈降级/热路径防刷屏）、
  Warning（功能受损主流程继续）、Error（数据可能不正确）、Fatal（启动不可继续），
  含热路径"一次性停用决策必须 Warning"等约束
- **新增陷阱 52**：PowerShell 5.1 `Get-Content`/`Set-Content` 批量改码触发
  GBK↔UTF-8 双重编码污染（本批真实踩坑，11 文件 `git checkout` 回滚后用
  结构化编辑工具重做；全程源码零遗留损坏）

验证：Release 构建 0 错误（24 警告=基线）；单测 156/156；publish 自包含部署
`D:\Tool\PChabit` 3.61.0.0；冒烟 PASS（版本匹配/存活 30s/主窗口正常/
0 ERR/FTL）；部署前用户运行实例按 SOP 关闭、部署后已恢复。

## [3.60.0] - 2026-11-06

### 变更：工程改造计划阶段 B——异常收口第一批

承接 `docs/工程改造计划.md`（阶段 A 安全网已于 4c6529d 落地）。本批为
**纯可观测性补丁**：所有 catch 块的控制流零变化（原来吞掉的现在记日志后
同样降级），仅消灭「无日志、无说明的静默吞错」。

- **新增「异常处理规约」**（`AI_MAINTENANCE.md`）：四类边界
  （UI 边界 / 可选外部 IO / 数据完整性 / 启动编排）+ 五条硬性规则
  （catch 必须有日志和注释、`catch(Exception)` 限类别 1/2/4、禁空 catch、
  禁用 Debug 记录「功能已停」、默认值不得冒充真实值）。
- **空 catch 清理 36 处**（基线 52 → 16，达成 ≤25 目标）：
  `AppIconService` 7、`ProcessResourceMonitor` 9、`HardwareMonitor` 5、
  `QuickNoteWindow` 5、`NetworkManager` 4、`AppInfoResolver` 4、
  `DataCollectionService` 3、`PerformanceCounterManager` 2、
  `DataManagementViewModel` 2、`DiskManager` 1。每处按规约标注
  「预期会发生什么异常 + 降级路径」，采集热路径用 Debug、
  整体失效风险用 Warning（如 `HardwareMonitor.UpdateAll` 主循环）。
- **`PChabit.HardwareMonitor` 补 Serilog 引用**：该采集模块此前无任何
  日志依赖（LiteMonitor 移植残留），异常完全不可观测。
- **`scripts/smoke_test.ps1` 日志盲区修复**：当日首启时今天的日志文件
  尚不存在，原实现基线阶段找不到即跳过整个 ERR/FTL 扫描——安全网
  形同虚设。现启动后重找一次（新文件基线为 0，语义不变）。
- 版本号说明：原计划阶段 B 为 3.58.0，因 3.57–3.59 已被「分析页精简」
  功能发布占用，按版本只增不减原则顺延为 **3.60.0**。

验证：Release 构建 0 错误（24 警告 = 既有基线）；单测 156/156；
publish 自包含部署 `D:\Tool\PChabit` 3.60.0.0；冒烟 PASS
（版本匹配 / 进程存活 30s / 主窗口正常 / **日志实测 0 ERR/FTL** /
正常关闭触发 Stop() 改动路径无异常）。

## [3.59.0] - 2026-11-06

### 修复:点击「应用使用」崩溃(3.58.0 回归)

3.58.0 为「使用时间占比」饼图新增口径切换 ComboBox(按应用/按分类)时,
XAML 里写了 `SelectedIndex="0"`,导致 `InitializeComponent()` 加载期间**同步触发**
`PieModeCombo_SelectionChanged`,而此时 `ViewModel` 尚未赋值(构造函数在 `InitializeComponent()`
之后才注入),处理器访问 `ViewModel.PieMode` 抛 NRE,被 WinUI 包装为
`Cannot create instance of type 'AppStatsTab'`,点击「应用使用」即崩溃。

修复:
- `AppStatsTab` 的 `PieModeCombo_SelectionChanged` 与 `RangeCombo_SelectionChanged`
  首行加 `if (ViewModel is null) return;` 守卫,规避 XAML 加载期同步事件触发空引用。
- 功能与 3.58.0 设计一致(默认按应用,可切按分类),无行为回退。

验证:Release 构建 0 错误;publish 自包含部署 `D:\Tool\PChabit` 3.59.0.0 后,
点「应用使用」正常打开,饼图口径「按应用 / 按分类」切换正常,不再崩溃。

## [3.58.0] - 2026-11-06

### 变更：分析页分类构成合并/删除

承接 [3.57.0] 分析页精简，按用户逐项确认的「方式 B」处理分类构成（应用/网页）：

- **应用分类构成并入应用使用页**：`AppStatsTab` 的「使用时间占比」饼图新增
  **口径切换**（ComboBox「按应用 / 按分类」，默认按应用）。按分类模式复用 Engine 已产出的
  `AppStatsEngine.CategoryShares`（按 `ResolveCategory` 聚合，含分类色），`AppStatsViewModel`
  新增 `PieMode` 状态并缓存最近 report——切口径时用缓存重算，无需重新查库
  （`GeneratePieChartData` → 拆出 `BuildPieChartData(report)`，按 `PieMode` 取
  `PieSlices`（按应用）或 `CategoryShares`（按分类））
- **网页分类构成直接删除**：分析页「构成双饼图」卡整块删除；`AnalyticsViewModel`
  删净 `CategoryShares`/`WebCategoryShares` 集合、`CompositionChartJson`、
  `HasComposition`/`HasCategories`/`HasWebCategories`、装载段与 `BuildCompositionChartJson()`
- **移除对应前端物**：删除 `CompositionPieView.xaml/.cs` 控件与
  `Assets/PieChart/composition.html` 模板（AnalysisEngine 底层 `WebCategoryShares`、
  `BuildWebCategoryShares` 保留，供周报 / AI 解读消费，不动底层）

验证：Release 构建 0 错误（沿用既有 24 条警告）；publish 自包含部署
`D:\Tool\PChabit` 3.58.0.0 后核对构成卡消失、应用使用页饼图口径切换正常。

## [3.57.0] - 2026-11-06

### 变更：分析页精简为单页（合并四页签，删除冗余功能）

用户反馈分析页数据过多且杂乱、实际用途不大，要求合并同类数据、删掉无用数据。本次改动：

- **单页化**：原「总览 / 节奏构成 / 硬件 / 应用流向」四个 Pivot 二级页签合并为**一个纵向滚动页**
  （`AnalyticsPage.xaml` 重写），顺序：KPI → 近 14 天活跃热力 → 24 小时四维分布 → 构成双饼图 →
  应用变化 → 硬件汇总与负载 → 应用流向 Sankey。删除 `Pivot` 与其 Tag 页签切换逻辑
- **KPI 卡精简为 4 项**：只保留 活跃时长 / 专注段 / 生产力占比 / 网页时长，删除
  夜间使用 / 键盘 / 输入密度 / 网页占比 / CPU日均 / GPU日均 / GPU峰值温度（VM 增
  `ShownKpis` 展示层过滤，Engine 底层 KPI 计算保留，供周报/AI 消费与测试）
- **删除多源数据摘要卡**（仪表盘「今日速览」已有类似功能）
- **删除顶部「复制周报」「导出 Excel」按钮**（未使用过；VM 删 `CopySummaryCommand`、
  code-behind 删 `ExportExcel_Click` 与其 Excel 导出逻辑）
- **彻底删除 AI 深度解读 + 洞察整块**（鸡肋）：VM 删 `AiItemViewModel`/`AiPlanViewModel`、
  全部 AI 命令（RunAiInsight/CancelAiInsight/CopyAiInsight/SendFollowUp/ApplyParsedAi/
  MarkPlanDone/MarkPlanSkipped）、`Insights` 集合、习惯养成轨迹 `HabitTrends`；code-behind
  删 `AiSuggestion_Tapped`/`PlanDone_Click`/`PlanSkip_Click`/`InsightCard_Tapped`
- **跨页导航改为锚点滚动**：`PivotNavigationRequested`/`AnalyticsNavArgs` 保留，但
  `ApplyPivot` 不再切换页签，改为滚动到节奏/构成卡并高亮（history → 跳「网页访问」历史页）
- **保留**：网页浏览历史入口、周期切换、刷新、热力图点击跳时间线、所有图表
  （构成双饼图 / 硬件趋势 / Sankey）

验证：Release 构建 0 错误（新增无警告，其余为既有 24 条），`x:Bind` 编译期全部通过；
publish 自包含部署 `D:\Tool\PChabit` 3.57.0.0 后人工核对单页滚动布局与 KPI 4 项展示。

## [3.56.0] - 2026-11-05

### 修复：便签超过 4 张后卡片被压缩

- `NotesPage.xaml` 的 `ItemsWrapGrid` 原先只设 `ItemWidth="248"`、未设
  `ItemHeight`。在 `Orientation="Horizontal"` 且 `ItemHeight` 为 NaN 时，
  `ItemsWrapGrid` 会把可用高度按行数重新分配：窗口宽度约容纳 4 列，第 5 张便签
  换行后行数由 1 变 2，每张卡片的高度随之变矮；行数越多压缩越明显，内容区
  被 `Grid` 的 `*` 行挤到 `MaxHeight="168"` 以下，可读行数明显减少
- 现补 `ItemHeight="248"`，卡片尺寸恒定（248×248），不再随便签数量变化；
  超出一屏由 `GridView` 自带滚动条纵向浏览。项目内
  `KeyboardDetailsTab.xaml` 的同类布局本就同时指定了两个尺寸，此次对齐
- 卡片内部 `Grid RowDefinitions="Auto,*,Auto"` 与 `RichTextBlock
  MaxHeight="168"` 保持不变，短便签留白、长便签截断的既有表现不受影响

## [3.55.0] - 2026-11-03

### 变更：便签支持 Markdown 样式显示

便签全面支持 Markdown 渲染（用户选项：Markdig + RichTextBlock 原生渲染、
卡片与编辑框双位置、尽量完整语法）：

- 新增 `Controls/MarkdownText.cs`：附加属性
  `<RichTextBlock controls:MarkdownText.Text="{x:Bind ...}" />`（RichTextBlock
  是密封类无法继承，故用附加属性挂接）。Markdig 0.45 `UseAdvancedExtensions`
  解析 + 遍历 AST 转 `Paragraph/Span/Run`；WinUI `RichTextBlock.Blocks` 只收
  `Paragraph`，代码块/引用/表格/分隔线用 `InlineUIContainer` 承载复合 UI
- 支持范围：标题 1–6、**粗体**、*斜体*、~~删除线~~、`行内代码`、代码块、
  嵌套有序/无序列表、任务列表（`TaskListInlineParser` 产出的行内节点渲染
  ☑/☐）、引用（嵌套，左侧竖线 Border + 内嵌 RichTextBlock）、链接（可点击
  Hyperlink）、图片（异步 BitmapImage，失败降级占位文本）、表格（Grid 等宽
  列 + 表头加粗底色）、分隔线；单换行即换行（`UseSoftlineBreakAsHardlineBreak`，
  便签录入习惯）；HTML 块/内联一律忽略（安全）
- 便签卡片（`NotesPage.xaml` 卡片 DataTemplate）与编辑对话框（新增
  「✏ 编辑 / 预览样式」RadioButton 切换，预览区 `ScrollViewer` + 渲染，
  打开对话框自动重置为编辑模式）两处接入；QuickNoteWindow 快速录入窗保持纯文本
- 颜色按目标控件 `ActualTheme` 深浅两套硬编码（附加属性回调里拿不到
  ThemeResource 的主题解析），`ActualThemeChanged` 重渲染；解析异常降级纯文本
- **顺带修一个既有缺陷**：「+ 新建便签」按钮只创建空便签入库、从不弹编辑框
  （`NewAsync` 只调 `BeginEditAsync`，`ShowAsync` 仅在点卡片的
  `OpenEditAsync` 路径上）。改由页面层 `NewNote_Click` 创建后立即
  `OpenEditAsync`，新建即进编辑
- **踩坑记录**（详见 AI_MAINTENANCE 陷阱 47）：x:Bind 初始化即触发附加属性
  回调，回调内任何未捕获异常（如颜色串解析越界）会沿
  `PropertyChangedCallback → SetValue → Loading` 冒泡成 App 级 FTL 崩溃
  （进便签页即闪退），Render 的 try-catch 救不了 try 块之外的代码

验证：Release 0 错误、单测 156/156、publish 部署 `D:\Tool\PChabit`
3.55.0.0；uiautomation 实测新建便签输入 Markdown → 预览样式六项全检出
（标题/粗体/列表/引用/☑/☐，源码符号不残留）→ 保存后卡片按样式渲染、
既有便签 URL 全部变为可点链接；新增 ERR/FTL=0

## [3.54.0] - 2026-10-31

### 变更：应用图标重设计（替换 3.51.0 的九宫格方案）

用户反馈图标"还是有点丑"。原图标为 3×3 橙/深青圆角方块阵 + 斜线节点，
在 16–32px 下元素过密、视觉重心分散。

新设计（`scripts/generate_icons.py` 重写，可复现）：

- 造型：深青纵向渐变圆角底（#16424D → #0A262E，圆角 22%）+
  三根递增橙色圆角柱（#F59E0B / #FBBF24）+ 青色趋势线与端点圆点
- 小尺寸优化：趋势线在 <48px 时自动省略，24/16px 仅保留三柱，
  保证任务栏与托盘清晰
- 4× 超采样 + LANCZOS 缩放，边缘干净无锯齿
- 一次生成全部产物：Assets 全套 PNG（含 Splash/宽幅/多 scale）、
  程序图标 `pchabit.ico`（16–256 七档）、托盘三态 ICO、浏览器扩展图标
- 托盘状态：运行=原色，暂停=降饱和 25%，禁用=灰度 + 降亮度

验证：Release 0 错误；部署 510/510、自包含校验通过；提取 exe 内嵌图标
与新设计一致（32px 无趋势线）；界面左上角 Logo 实拍更新。

## [3.53.0] - 2026-10-30

### 修复：仪表盘「专注 / 应用切换 / 活跃时段」恒为「--」

用户反馈仪表盘这三项没有数据。查库实证定位到两个独立原因：

- **写入端缺失**：`DailySummaries.FocusMinutesV2` 与 `AppSwitches` 两列
  在全库 63 行中**全部为 NULL**——只有读取方（仪表盘 / 习惯画像 /
  个人基线），`AggregateDailySummaryAsync` 从未赋值
- **今日不聚合**：每日聚合挂在 `CheckDailyAggregationAsync`，只处理
  「前一天」；今日行通常只是网络流量写入的空壳（`ActiveMinutes=0`、
  无首末活跃），因此仪表盘读今日行时三项全部缺失

修复：

- `DataCollectionService.Maintenance.cs`：聚合时补写
  `FocusMinutesV2`（生产力分类活跃分钟）与 `AppSwitches`
  （`会话数 - 1`，与 `EfficiencyCalculator` 切换口径一致）
- `DashboardViewModel`：聚合值缺失时用已加载的今日 `AppSessions`
  实时补齐三项（符合「今日数据必须实时合并」约束，不写库）
- 聚合 `incomplete` 判定追加 `FocusMinutesV2 == null && ActiveMinutes > 0`，
  昨日历史行在下一次每小时检查时自动补算一次
- 专注口径调整：`AppSession` 是前台窗口会话，切窗即断，实测单会话
  最长仅 21 分钟，保留「单会话 ≥25 分钟」门槛会恒为 0；改为
  **生产力分类的活跃分钟之和**

验证：Release 0 错误；全量部署 510/510、自包含校验通过；
uiautomation 实拍仪表盘显示「专注 148 分钟 / 应用切换 1364 次 /
活跃时段 05:35 – 20:34」，日志无新增 ERR/FTL。

## [3.52.0] - 2026-10-15

### 修复：硬件监控 + 任务栏插件异常（双实例并存根因 + 两处显示 bug）

用户反馈「硬件监控异常、任务栏插件显示异常」，根因排查结论：

- **根因 1（主因）：单实例互斥仅会话内生效，跨会话双实例并存**
  - `Local\PChabit_SingleInstance` Mutex 只阻止同一终端会话内的第二个实例；
    当存在跨会话残留实例（如 RDP/切用户留下的进程）时，两实例同时通过
    HttpListener 绑定 8765 端口，后启动实例 `WebSocketServer.StartAsync` 抛异常，
    被 `StartMonitoring` 的单一大 try 块捕获 → **后续硬件监控服务、任务栏/
    托盘显示定时器、桌面悬浮插件、HardwareSampleWriter 全部未启动**，
    表现为硬件监控页无数据/不刷新、任务栏插件不挂载。
  - 修复：Mutex 前缀 `Local\` → `Global\` 实现跨会话互斥；另新增命名事件
    `Global\PChabit_SingleInstance_Activate`，第二实例启动时唤起已有实例窗口
    而非仅静默退出。
- **根因 2：WebSocket 启动失败不应中断整条监控链**
  - `MonitorManager.StartAllAsync` 中 WebSocket 启动改为 try/catch 容错：
    端口被占时仅降级「网页监控不可用」，硬件监控/任务栏/桌面插件继续启动。
- **根因 3：硬件监控页内存容量单位错误**
  - `MEM.Used` 传感器实测单位为 GB（如 16.9），ViewModel 按 MB 格式化 →
    显示「17 MB / 31.8 GB」（应为「16.9 GB / 31.8 GB」）。已加单位启发式
    （<1024 视为 GB ×1024 转 MB）修正。
- 已知说明：CPU 温度显示 `--` 为 LHM 传感器无读数的设计降级（说明文案已注明
  「无读数时为灰色且进度条为空」），非本次回归。

部署 `D:\Tool\PChabit`（FileVersion 3.52.0.0）、冒烟验证硬件监控与任务栏插件
数据正常刷新、日志无新增错误。

## [3.51.0] - 2026-10-15

### 变更：应用图标整体重设计

用户反馈原图标「太难看了」（白底蓝色显示器轮廓 + PChabit 文字 + 右下角
「豆包AI生成」角标），本次全新重设计：

- 新图标方向：键帽趋势标（候选B）—— 3×3 键帽矩阵（暖橙 + 深绿相间），
  右上键帽上的上升趋势折线 + 青色端点（寓意「电脑使用习惯的持续养成」），
  右侧圆润模块感字标「PChabit」（P 橙色、其余深绿）
- 替换全部图标位图资产：
  - `pchabit.ico` 多尺寸（16/24/32/48/64/128/256，透明底）
  - `Assets\*.png` 13 个打包位图（Logo/Square150x150/Square44x44/
    StoreLogo/Wide310x150/LockScreenLogo 及 scale-200 变体，透明底；
    SplashScreen 为白底版）
  - 根目录 `LOGO.png`（2048×2048 白底完整版，去除原「豆包AI生成」角标）
- 图标同时作用于：exe 资源图标（ApplicationIcon）、窗口标题栏图标、
  系统托盘图标、侧边栏 Logo、开始菜单/任务栏打包图标

## [3.50.0] - 2026-10-15

### 变更：「应用使用 › 键鼠详情」键盘统计与鼠标统计合并为同一板块

原「键盘统计」「鼠标统计」为两个独立标题区域（各带一组指标卡），本次合并为
一个统一的「键鼠统计」板块：

- `KeyboardDetailsTab.xaml` 合并两区域为一个卡片板块：
  - 板块标题「键鼠统计」（键盘 + 鼠标双图标），不再分两个小标题
  - 指标卡统一为 5 列 × 2 行网格：行 0 = 键盘 4 项（总按键次数/平均速度/
    峰值速度/快捷键使用）+ 鼠标总点击次数；行 1 = 鼠标 5 项（左键/右键/
    中键/移动距离/滚动次数），保留原鼠标指标的彩色数字
  - 平均点击间隔横条并入板块底部
  - 按键使用排行、编辑统计、常用快捷键并入板块内（次级卡片背景区分层级）
- 数据与 ViewModel 无任何改动（`KeyboardDetailsViewModel` 全部绑定属性原样保留）

### 变更：「分析」页面「节奏」与「构成」两页签合并为「节奏构成」

原「节奏」（日历热力 + 24h 分布）与「构成」（双饼图 + 应用变化）为两个独立页签，
本次合并为一个「节奏构成」页签：

- `AnalyticsPage.xaml` 删除「构成」PivotItem，其双饼图（分类构成 + 网页分类构成）
  与「应用变化」卡片并入「节奏构成」页签底部；页面自上而下：
  近 14 天活跃热力 → 24 小时四维分布 → 分类构成双饼图 → 应用变化
- 页签 Header 改为「节奏构成」，Tag 保留 `rhythm`；洞察 actionKey
  `rhythm` / `compose` 均归一化定位到该页签（`ApplyPivot`），`ComposeCard`
  随内容保留在合并页签内

## [3.49.0] - 2026-10-15

### 变更：硬件监控入口移回左侧导航

3.35.0 时「硬件监控」从侧栏移到了「设置 › 监控设置」的跳转按钮，本次按
使用习惯移回左侧导航：

- `ShellPage.xaml` 恢复「系统」分组与「硬件监控」导航项（Tag=`Monitor`，
  原 glyph E950）；导航 key 从未移除，点击即打开原页面
- 删除设置页「打开硬件监控 →」按钮及 `OpenHardwareMonitor_Click`
- 桌面悬浮插件双击入口不受影响（同样走 `NavigateTo("Monitor")`）

## [3.48.0] - 2026-10-15

### 变更：分析-构成页改为双饼图面板

「分析 › 构成」页的「分类构成」与「网页分类构成」由原来的两张横向
进度条列表，合并为**一个面板内的左右两个环形饼图**（WebView2 + ECharts）：

- 新增 `Assets/PieChart/composition.html`：单页双饼图，标题内嵌、跟随系统
  深浅色主题，单侧无数据时该侧显示「暂无数据」
- 新增 `Views/CompositionPieView` UserControl（`ChartJson` 依赖属性），
  模式与 `HardwareTrendView` 一致；仅一个 WebView2 实例承载双图
- 两个饼图共用「分类名 → 颜色」映射，同一分类两侧同色，便于本机 vs 线上对比；
  悬停显示时长（小时/分钟）与占比
- `AnalyticsViewModel` 新增 `CompositionChartJson` / `HasComposition`；
  原 `CategoryShares`/`WebCategoryShares` 集合保留（Excel 导出等仍在用）

## [3.47.0] - 2026-10-13

### 维护：全项目死代码清理（不影响功能）

对解决方案做了一次系统性大扫除，删除确认无引用的死代码、临时文件与构建残留，
用户可见行为零变化：

- 删除 7 个零引用整文件：`ShortcutDetector`、`TypingBurstDetector`、
  `SerilogConfigurator`、`PerformanceMonitor`（含 `IPerformanceMonitor`/
  `PerformanceMetrics`）、`TaskScheduler`（含 `IBackgroundTaskQueue`/
  `BackgroundTaskQueue`）、`CacheService`（含 `ICacheService`/
  `MemoryCacheService`/`CacheKeys`）、`DataManagementViewModel.History.cs`
- `ServiceConfiguration` 移除 `ICacheService`/`MemoryCacheService` 注册与
  `AddMemoryCache()`；`PChabit.Infrastructure.csproj` 移除未被使用的
  `Microsoft.Win32.Registry` 包引用（NU1510）
- 转换器清理：删除 3 个未注册转换器与 13 个「已注册但无 XAML 绑定」转换器
  （共 −315 行），`App.xaml.cs` 的 `RegisterAppConverters()` 同步删 13 行注册
- ViewModel 清理：`SettingsViewModel` 删 5 个 RelayCommand 及 `_monitorManager`
  注入、`_connectedBrowsers`；`DashboardViewModel` 删 5 个只写属性；
  `WebHistorySectionViewModel` 删 `_statusMessage`（7 处赋值全部无绑定）；
  `AnalyticsViewModel`/`NotesViewModel`/`CategoryCrudViewModelBase`/
  `WebDetailsViewModel`/`DataManagementViewModel` 各删失效命令与只写字段
- `KeyboardDetailsViewModel` 删除 XAML 未绑定的 `HourlyData`/
  `HourlyMouseStats` 集合及填充链路（实际渲染走 `HourlyKeyMouseCells`），
  `ComputeHourlyData` 去掉无人使用的 `AvgSpeed` 投影
- 全局操作日志 `GlobalOpLog` 删除随书签同步下线的 `ReadTodayFile()`/
  `ExportToday()`
- 构建警告由 52 行降至 22 行（剩余为既有非死代码警告）；单元测试 156/156 通过

## [3.46.0] - 2026-10-13

### 变更：「按应用键鼠统计」并入应用详情弹窗

原「应用使用 › 键鼠详情」页的「按应用键鼠统计」卡片（以应用为主体、每行聚合
键盘 KPM/按键 + 鼠标 点击/移动/滚动）已删除，整体迁移进应用排行点击「详情」
弹出的 `AppDetailDialog`，作为单应用详情的「键鼠统计」卡片：

- 卡片为五列指标（按键 / KPM / 点击 / 移动 / 滚动），任一侧无数据显示 `—`；
  键鼠两侧都无数据时卡片自动隐藏
- `AppDetailEngine` 新增 `MouseSessions` 聚合查询（点击 = 左+右+中键，
  移动距离、滚动次数，与键鼠页口径一致），`AppDetailReport` 新增
  `MouseClicks` / `MouseMoveDistance` / `MouseScrolls` / `HasKeyMouse`
  及五个 `*Text` 格式化属性；`Compute` 新增三个可选参数（默认 null，向后兼容）
- 原「输入」卡片（仅按键 + KPM）被「键鼠统计」卡片取代，信息为其超集；
  复制摘要的输出行由「输入」改为「键鼠」五项
- 同步清理 `KeyboardDetailsViewModel` 中随之失效的成员：`AppKeyMouseRows` /
  `AppKeyMouseItem` / `BuildAppKeyMouseRows` / `AppKeyUsage` / `AppKeyItem` /
  `ProgramMouseStats` / `ProgramMouseStat` / `ComputeAppKeyUsage` /
  `ComputeProgramMouseStats` / `NormalizeProcess`，以及只为它们服务的
  分类快照加载代码（按应用维度的分类解析已随卡片一起迁走）

### 修复：应用详情「输入/键鼠」统计恒为空

根因：`AppDetailEngine.BuildAsync` 的键盘查询写的是
`k.Date.CompareTo("yyyy-MM-dd")`——`DateTime` 没有 `CompareTo(string)` 重载，
实际绑定到 `CompareTo(object)`，EF Core 无法翻译该调用；异常又被既有的
`try-catch` 静默吞掉，`keyPresses` 永远落回 0，「输入」卡片因此从不显示。
改为与 `KeyboardDetailsViewModel` 一致的 `DateTime` 范围比较
（`k.Date >= period.Start.Date && k.Date < period.EndExclusive.Date`）。
新增的鼠标查询直接采用正确写法。

### 验证

- Release 构建 0 错误（44 警告均为既有）；单测 **156/156** 通过
  （新增 `Compute_WithMouse_KeyMouseFlagsAndTexts`、`Compute_KeyAndMouse_BothSidesTexts`）
- publish 产物 FileVersion = 3.46.0.0（461 文件，语言目录收敛 zh-CN/en-us）；
  部署 `D:\Tool\PChabit`（805 文件，源/目标 DLL 时间戳与字节数一致）
- `scripts/verify_346.py` 冒烟（uiautomation 实点）：
  - 应用排行 → 详情弹窗出现「键鼠统计」卡片，实测五项
    `按键 290 / KPM 218 / 点击 941 / 移动 — / 滚动 3,742`；
    旧「输入」卡片标题已不存在（精确匹配为 False）
  - 「移动」显示「—」经数据库核实为**采集侧数据缺失**：`MouseSessions.TotalMoveDistance`
    自 2026-09-16 起全部落 0（最后非零日 09-16，此前 6-20~9-16 有 21 天非零），
    与本次改动无关；缺失侧按预期显示 `—`
  - 键鼠详情页「按应用键鼠统计」已消失（False），「每小时键鼠统计」「鼠标统计」
    分区保留（True）
  - 新增 ERR / FTL = 0

## [3.45.0] - 2026-10-13

### 修复：应用分类收口为全项目唯一一套逻辑（键鼠页与应用排行不再打架）

用户反馈「按应用键鼠统计」里的应用分类与「应用使用排行」不一致。

根因：`KeyboardDetailsViewModel` 自己 `new AppSession { ProcessName = ... }` 去调
`AppStatsEngine.ResolveCategory`，这个临时对象没有 `Category` 字段，于是解析链第二级
「会话快照分类」恒为空 → 只要该进程不在用户映射表里，键鼠页就一律显示「未分类」，
而应用排行读的是真实会话快照。实测近 7 天有 4 个进程因此分叉
（`Min.exe` / `SearchHost.exe` / `ImeService.exe` / `PassTC.exe`，
应用排行分别为 AI 助手 / 系统工具 / 其他 / 其他，键鼠页全是「未分类」）。

- `AppStatsEngine` 新增**唯一分类解析入口**，全应用任何展示/统计场景都必须走它：
  - `ResolveCategoryByProcess(processName, categoryMap, snapshotCategory)`：
    用户映射表 → 会话快照分类 → `未分类`
  - `ResolveCategoryWithSnapshot(processName, categoryMap, snapshotMap)`
  - `LoadSnapshotCategoryMapAsync(db | dbFactory, start, endExclusive)`：
    归一化进程名 → 该周期**最早**一条会话的 `Category`（与 `Compute` 里 `g.First()` 取法一致，
    保证按进程聚合的页面与「应用使用排行」同源）
  - 常量 `UncategorizedName`；原 `ResolveCategory(AppSession, map)` 改为委派到统一入口
- `KeyboardDetailsViewModel`：键盘侧与鼠标侧都走统一入口（键盘行此前完全没有分类字段，
  只在同时有鼠标行时才带出，现已补上）；顺带删掉 `ComputeProgramMouseStatsAsync`
  里对 `MouseSessions` 的重复查询（同一份数据查了两遍）
- `NetworkTrafficViewModel`、`App.DesktopWidget`：各自手写的
  `TryGetValue + "未分类"` 兜底删除，改调统一入口（悬浮插件空间有限，
  未分类仍只显示进程名，这属于展示裁剪而非第二套逻辑）

### 变更：删除键鼠详情「鼠标详细记录」卡片

该卡片逐小时罗列左/右/中键点击与移动/滚动，与上方「每小时键鼠统计」四方格、
「按应用键鼠统计」完全重复，没有增量信息。同步删除 `MouseClickDetails` 集合、
`MousePart.ClickDetails`、`ComputeMouseClickDetails` 与 `MouseClickDetail` 模型类。

### 修复：应用排行「详情」弹窗底部内容被截断

根因是布局：根 `Grid`（`MaxHeight="560"`）里直接放 `StackPanel > ScrollViewer`，
`StackPanel` 会把「期望高度」（无限）透传给 `ScrollViewer`，
于是内容超出 560px 后**既不出滚动条也被裁掉**——「日趋势」「常用窗口 Top」等卡片看不见。
改为 `Grid` 三行（头部 `Auto` / 加载态 `Auto` / 内容区 `*`），内容区拿到确定高度即可正常滚动。

### 验证

- Release 构建 0 错误（24 警告，均为既有）；单测 154/154 通过
- publish 467 文件，`PChabit.dll` FileVersion = **3.45.0.0**，时间戳 2026-10-13 06:56；
  `deploy_to_tool.py` 部署 `D:\Tool\PChabit`（目标 DLL 时间戳与 publish 一致）
- `scripts/verify_345.py` 冒烟（uiautomation 实点）：
  - 键鼠详情页「按应用键鼠统计」正常渲染，行内分类标签实测为
    `系统工具 / AI 助手 / 系统工具 / 办公 / …`（**不再是清一色「未分类」**）
  - 「鼠标详细记录」卡片已消失（页面文本检测 `鼠标详细记录 not in t` 为 True）
  - 应用排行 → 详情弹窗：`日趋势`、`小时分布`、`详细说明` 均在可视区内；
    向下滚动后 `常用窗口 Top` 也进入可视区（此前该卡片根本无法看到，因容器不给高度）
  - 新增 ERR / FTL = 0
- 数据库口径复核（真实库）：旧逻辑会对 `PassTC.exe` / `Min.exe` / `LM Studio.exe`
  显示「未分类」，新逻辑分别解析为「其他 / AI 助手 / AI 助手」，与应用排行一致

### 顺带收口：全库同类分叉一并修掉（不止用户报的那一处）

修完键鼠页后按「有分叉的地方一定不止一处」的原则全库复查，又查出 **3 处同源问题**：

1. **桑基图**（`SankeyViewModel`）：同样用 `new AppSession { ProcessName = ... }` 空壳调解析，
   空壳的 `Category` 恒为空 → 只要进程不在映射表就显示「未分类」。已改走
   `ResolveCategoryWithSnapshot`（加载快照表覆盖所选日期范围）。
2. **详情弹窗把「已解析的分类名」当快照喂回解析链**（`AppStatsTab.xaml.cs` 调
   `AppDetailViewModel.Configure` 时传的是与 `AppDetailEngine.BuildAsync` 同名的 `category` 参数）：
   弹窗内的「总时长/会话」等详情统计其实按 `category` 再解析一遍，
   一旦传进去的是「未分类」，它会被当成兜底值**覆盖掉真实分类**。
   现已改为传新增的 `AppStatItem.FallbackCategory`（映射未命中时的原始快照分类）。
3. **兜底字面量 `"未分类"` 散落 6 处**（`AppDetailViewModel` 2 处、`AnalyticsEngine`、
   `AppDetailEngine`、`AiPromptExportFormatter`、`AiCommandDispatcher` 3 处）——
   只要有第二个兜底值出现，就必然出现第二套口径。现全部改为引用
   `AppStatsEngine.UncategorizedName` 常量；`grep -rn '"未分类"' src/` 只应命中该常量定义处。

---

## [3.44.0] - 2026-10-13

### 变更：键鼠详情「应用按键统计」+「按程序鼠标统计」合并为「按应用键鼠统计」

原两张卡分别只列键盘或只列鼠标，同一应用要上下对照看两次。合并为一张以应用为主体的表：
每行同时给出按键次数 / KPM / 点击 / 移动 / 滚动，缺失的一侧显示「—」；
两侧按归一化进程名（去 `.exe`、小写、去空格）合并。

---

## [3.43.0] - 2026-10-13

### 变更：键鼠详情页「每小时键鼠统计」长条图合并为四方格

用户反馈键鼠详情页的「每小时按键分布」与「每小时鼠标统计」两张长条状图表
过于占用空间：改为一张全宽「每小时键鼠统计」卡片，每小时一格四方格（12 列 × 2 行），
每格上方为时段标签（00:00~23:00），下方两个数字——左侧按键次数、右侧蓝色鼠标点击次数，
空格即补 0，占空间大幅缩小；鼠标移动/滚动等明细保留在格子悬停 ToolTip 中。

- `KeyboardDetailsViewModel`：新增 `HourlyKeyMouseCell`（Hour/KeyPresses/MouseClicks/ToolTip）
  与 `HourlyKeyMouseCells` 集合，键盘（`HourlyKeyItem.Hour` "8:00" 格式）与鼠标
  （`HourlyMouseStat.Hour` int）按小时双向匹配合并，遍历 0..23 固定生成 24 格；
  移除不再使用的 `HourlyKeyRows` / `HourlyMouseRows`（3.35.0 长条图数据投影）
- `KeyboardDetailsTab.xaml`：删除两处 `HourlyBarChart` 长条图卡片；
  新增全宽「每小时键鼠统计」四方格卡片（ItemsControl + ItemsWrapGrid 12 列）；
  原「应用按键统计」「按程序鼠标统计」卡片改为占整行
- 版本：3.42.0 → 3.43.0

### 验证

- Release 构建 0 错误；单测 154/154 通过
- publish 部署 `D:\Tool\PChabit`（3.43.0.0，749 文件，DLL FileVersion 已核验）
- `scripts\verify_343_hourly_grid.py` 冒烟通过：进入键鼠详情页、「每小时键鼠统计」卡存在、
  旧「每小时按键分布」/「每小时鼠标统计」标题不存在、24 时段标签渲染、日志新增 ERR/FTL=0
- 截图复核：键盘统计 KPI/按键排行/编辑统计区渲染正常；
  四方格 12 列 × 2 行，时段 00:00~23:00，每格左=按键次数右=蓝色鼠标次数，数据按时段正确对齐

---

## [3.42.0] - 2026-10-13

### 变更：AI 命令接口（/api HTTP 路由，AI 可直接操控软件）

用户提出 AI 不应靠截图 + 模拟键鼠来操控软件，应能直接以命令形式调用。
复用现有 WebSocketServer 的 8765 端口（HttpListener），新增 `/api/*` HTTP 路由，
由 `AiCommandDispatcher` 分发：查询、写操作、设置修改全覆盖，返回 JSON。

- 命令表（`http://localhost:8765/api/...`）：
  - `GET /api/health` — 服务健康 + 版本 + PID
  - `GET /api/apps/stats?period=today` — 应用统计（totalMinutes / activeApps / topApp / apps 排行 / 分类构成）
  - `GET /api/apps/detail?process=xx&period=today` — 单应用详情（时长/会话/键鼠/硬件/小时分布/日趋势）
  - `GET /api/apps/category?process=xx` / `PUT {"process","category"}` — 查询 / 修改应用分类
    （空 category = 取消分类删除映射）
  - `GET /api/apps/description?process=xx` / `PUT {"process","description"}` — 查询 / 修改应用说明（3.41.0 功能）
  - `GET /api/summary?period=today` — 分析汇总（KPI / 构成 / 洞察 / 键鼠网页硬件流量指标）
  - `GET /api/settings` / `PUT {"key","value"}` — 读取全部设置 / 修改单个设置
    （白名单反射 + 类型转换，保存即生效并触发 UI 同步）
- 周期参数：today / yesterday / thisweek / lastweek / last7days / last30days / thismonth
- 实现：
  - `AiCommandDispatcher`（Infrastructure.Services）：命令路由 + JSON 响应 +
    统一 400/404/500 错误处理，注入 IDbContextFactory / ISettingsService / IServiceScopeFactory
  - `WebSocketServer`：GET 分支按 `/api/` 前缀转 dispatcher，非 /api 仍走设置页；
    构造注入 dispatcher（DI 工厂在 ServiceConfiguration 注册 Singleton）
  - 复用既有引擎与服务：AppStatsEngine / AppDetailEngine / AnalyticsEngine /
    ICategoryService / IAppDescriptionService / ISettingsService（设置反射白名单）
- 版本：3.41.0 → 3.42.0

### 验证

- Release 构建 0 错误；单测 154/154 通过
- publish 部署 `D:\Tool\PChabit`（3.42.0.0，749 文件，DLL FileVersion 已核验）
- `scripts\verify_342_ai_api.py` 全命令冒烟通过（11 项 PASS）：
  health 版本 3.42.0、stats/detail/summary/settings 查询、category/description/settings
  写入链路（写入→读回→恢复原值）、404/400 错误路径、日志尾部 ERR/FTL=0
- 说明：接口仅绑定本机回环（localhost:8765），信任级别与浏览器同步 WebSocket 一致

---

## [3.41.0] - 2026-10-13

### 变更：应用详细说明（预置库 + 用户可编辑）

按需求为应用添加「详细说明」：基于本机实际安装软件（注册表 + D:\Tool + E:\Game）
批量撰写内置预置说明库，在现有「应用使用」页的应用列表与详情弹窗中展示说明，
并允许用户手动修改（修改后转为用户条目，预置种子不再覆盖）。

- 数据层：新增 `AppDescriptions` 表（规范化进程名唯一索引 + 说明 + `IsUserEdited` 标记）
  - `DefaultAppDescriptions` 预置库：65 条中文说明，覆盖 AI 助手 / 开发 / 浏览器 /
    沟通 / 影音 / 下载 / 系统工具 / 游戏 / 办公 9 大类本机实际软件
  - `AppDescriptionService`：查询（单条 / 批量）/ 保存（upsert 置用户标记）/ 启动种子
    （跳过用户编辑行，其余增量补齐）
  - 建表走 DatabaseInitializer 手动迁移（`MigrateAppDescriptionTablesAsync`），
    与 EF 模型双路径一致（沿用陷阱 12 / 20 模式）
- UI：应用排行列表行在名称下显示说明（一行省略）；应用详情弹窗新增「详细说明」
  卡片（完整正文 + 内联编辑：编辑说明 / 保存 / 取消，见陷阱 41）
- 版本：3.40.0 → 3.41.0

### 验证

- Release 构建 0 错误；单测 154/154 通过
- publish 部署 `D:\Tool\PChabit`（3.41.0.0，467 程序文件）
- UI Automation 冒烟：列表说明展示、详情弹窗说明卡片、内联编辑保存链路
  （UI 更新 + 数据库 `IsUserEdited=1` + 日志）均通过；新增 ERR/FTL = 0
- 数据库核查：AppDescriptions 65 行种子数据，关键进程全命中

---

## [3.40.0] - 2026-10-12

### 变更：仪表盘卡片合并（按验收反馈迭代）

用户反馈仪表盘信息分散，信息量不大的内容各自独占独立卡片空间。
本次把「应用使用 Top 5」与「网站访问 Top 5」两张独立全宽卡片
合并为一张双栏卡片，同时把中部「最常访问的网站」并入
「最常使用的应用（含分类分布）」卡片：

- 「应用使用 Top 5」+「网站访问 Top 5」→ 一张卡片左右两栏，
  各栏保留标题与「查看全部 →」跳转入口（应用统计页 / 网页访问页）
- 中部「最常访问的网站」并入「最常使用的应用」卡片右侧，
  左栏应用图标 + 分类分布、右栏最常访问网站，卡片间加分隔线
- 纯布局合并：无绑定变更、无 ViewModel 改动，数据渲染与跳转行为不变

### 验证

- Release 构建 0 错误；publish 部署 `D:\Tool\PChabit`
- 截图 + UI Automation 确认：双栏标题、列表项（应用/网站各 Top 5）数据完整渲染

---

## [3.39.0] - 2026-10-11

### 变更：还原浏览历史域名的点击下钻

3.36.0 把原「网页浏览历史」原生化时只保留了域名聚合表，
**漏掉了原HTML 报告最核心的交互——点击域名查看该域名的详细情况**
（原 `report.js` 的 `showDomainDetail`）。本次按原实现完整还原：

| 原报告 | 本版 |
|---|---|
| 域名可点击（`.domain-link` 事件委托） | `ListView.IsItemClickEnabled` + `ItemClick` |
| 总访问次数 / 独立页面 / 首次访问 / 最后访问 | 同名 4 张统计卡片 |
| 每日访问趋势（Chart.js 折线） | 原生柱状（统一画风） |
| 24 小时时段分布（Chart.js 柱状） | 复用共享控件 `HourlyBarChart` |
| URL 列表（按访问次数取前 20） | 同（标题 + URL + 次数 + 最近访问） |
| ESC / 点遮罩关闭 | ContentDialog 默认行为 |
| — | 新增「在浏览器中打开」按钮 |

**顺带修一处计数口径错误**（查库时发现）：
`BrowserHistoryItem.VisitCount` 存在虚高——表行数 25208，但
`sum(VisitCount)` 为 94165（虚高 3.7 倍）。根因是该字段在入库时会被
「跨批次重复推送」累加（`HistoryIngestService.IngestCoreAsync`：
同一 `Url|VisitTime` 再次推送时 `VisitCount += n`），
实测 `https://www.google.com/` 单条记录累积到 **2074**。
它是入库侧的重复计数而非真实访问次数，全链路改为按「记录条数」统计，
文案统一为「记录数」以免误导。

技术要点：

- `UserControl` 只能有单一 `Content`（WMC0035），
  主卡片与详情 `ContentDialog` 须用 `Grid` 包一层承载
- 弹窗 `Rows` 绑定不能传数组：VM 属性须声明为
  `IReadOnlyList<HourlyBarRow>`，否则 WMC1121 拒绝隐式转换
- 每日趋势改原生柱状后需按当日峰值归一化出 `CountHeight` 才能渲染高低

### 变更：每日访问趋势并入活动时间分布（按验收反馈迭代）

网页访问页的「每日访问趋势」与「活动时间分布」都是时间维度，
但按天访问数价值有限且已隐含在活动分布的小时「访问」列与访问排行中，
单独占一张卡意义不高 → 两卡合为一卡。

- 删除独立的「每日访问趋势」卡片
- 「活动时间分布」标题后追加括号总数「（每日访问 N 次 · M 域名）」，
  每日访问不再独占一列
- 卡片内两列：左「小时分布」（应用+网页小时活动，原有内容），
  右「最近访问记录」（原独立卡片并入，含「加载更多」）
- 卡片副标题更新为「按所选时间段统计 · 左：应用使用与网页浏览的小时活动；右：最近访问记录」

### 验证

- Release 构建 0 错误；单元测试 154/154 通过
- publish 后部署 `D:\Tool\PChabit`，截图确认布局生效、数据正常渲染

---

### 修复：浏览历史「域名明细」是死数据（已停更 20 天）

**现象**：网页访问 › 浏览历史 › 域名明细的数据自 2026-09-20 起不再变化，
无论刷新多少次都是同样 1293 个域名 / 25101 条记录。

**根因**：`HistoryIngestService.IngestAsync` 的开关判断是

```csharp
if (!_settings.BrowserSyncEnabled || !_settings.BrowserHistoryIngestEnabled)
```

而本机 `settings.json` 里 `BrowserSyncEnabled = false`（它是「浏览器同步总开关」，
管的是 WebDAV 云端合并那条链路）。于是**关掉云端同步，连本机扩展推送的实时入库
也被一起掐断** —— 而这条链路根本不依赖 WebDAV。

之所以查不出来，有三个叠加原因：

1. 被跳过的分支只记 `Log.Debug`，而默认日志级别看不到，等于完全静默
2. 界面上**已无任何开关**能改回 `BrowserSyncEnabled`（该 UI 早已移除，
   `settings.json` 里的值成了不可见的单向门）
3. 页面只显示「历史库 25103 条」这类总量，看起来一切正常，没有任何异常提示

**修复**：

- `IngestAsync` 只判断专用开关 `BrowserHistoryIngestEnabled`，
  不再被云端同步总开关连带掐断（`HistorySyncService` 走 WebDAV 的那条路径保持原判断）
- 跳过分支从 `Log.Debug` 提到 `Log.Warning`，并在消息里说明「数据将停止更新」
- 浏览历史区块新增**陈旧告警条**：超过 2 天无新记录即显示，
  区分两种原因——采集被关（给「开启采集」按钮）/ 扩展掉线（提示检查授权）；
  采集开关在界面上无处可改时，告警条本身就是恢复入口

### 验证

- 核实数据库：`BrowserHistoryItems` 最新记录 2026-09-20 04:13，
  而同期 `WebSessions` / `AppSessions` / `NetworkTrafficSamples` 均更新到当日 21:xx
  → 确认只有历史库停更，且扩展当前处于已连接状态（21:35 有连接日志）
- Release 构建 0 错误；单元测试 154/154 通过
- publish 471 文件、3.38.0.0、部署 `D:\Tool\PChabit`
- 修复后扩展推送即恢复入库（详见下方验证记录）

## [3.37.0] - 2026-10-10

### 变更：网页访问页活动数据收敛为4 张卡片

原先 6 张卡片里有两处是「同一份数据的两个视角」，信息分散且重复占位。

**① 网站访问排行 + 网页流量归因 → 合并为一张表**

两者本就是**同一批域名**：排行按会话数/时长排序，流量归因把浏览器下行流量
按各域名在该小时的时长占比分摊。流量此前单独占一张 87 行的卡片，实际只是多一列数字。

- 新增 `DomainActivityRow`：域名 / 访问次数 / 停留时长 / 下行流量 / 流量占比
- 排行表加两列（下行流量、占比）与表头，估算口径说明下移到表尾
- 删除独立的「网页流量归因（估算）」卡片
- 合并逻辑在 `WebDetailsViewModel.BuildDomainActivity`，流量为 0 的域名仍保留（显示 "—"）

**② 每小时活动分布 → 改为「活动时间分布」（应用 + 网页双维度）**

原先网页页只显示网页侧的小时分布，应用的小时分布在另一个页面，
但两者都属于「活动」范围，且都应按用户所选时间段统计。

- 新增 `HourlyActivityCombinedRow`：时段 / 合计 / 应用 / 网页 / 访问次数
- 新增 `LoadAppHourlyAsync`：按所选时间段聚合 `AppSessions` 的小时分布，
  口径与应用统计页一致（均按 `StartTime.Hour` 归集）
- 应用侧查询失败时降级为仅显示网页（catch 后记日志，不影响主流程）

**顺带清理**：合并后 `DomainStats` / `HourlyActivity` / `TrafficAttributionItems`
三个可观察集合已无 UI 消费者，连同 `TrafficEmptyText` 一并删除。

### 验证

- Release 构建 0 错误；单元测试 154/154 通过
- publish 后部署 `D:\Tool\PChabit`
- 截图确认：排行表含流量两列、耗时分布卡片同时显示应用与网页、新增 ERR/FTL = 0

---

## [3.36.0] - 2026-10-10

### 变更：网页访问页两个页签合并为单页，历史内容原生化

「网页访问」下的「访问统计」与「网页浏览历史」画风割裂——前者是跟随系统主题的
WinUI 原生卡片，后者是 WebView2 加载 `report.html`，**浅色硬编码样式**
（`#f3f4f6` 背景、白卡片投影、24px 标题），深色模式下尤其突兀；且它是独立页签，
只能整套替换、无法融入统计页。

- 去掉二级页签，「网页访问」变为单页
- 新增 `WebHistorySection`：把历史库的**域名明细**（记录数 / 唯一 URL / 占比 / 最近访问）
  用原生卡片重写，含搜索与 CSV 导出（保留原报告的导出能力）
- 新增 `WebHistorySectionViewModel`，统计口径与原 `analyzeHistoryData` 一致
  （按域名聚合，`VisitTime` 为 Unix 毫秒需转本地时间）
- 删除 `HistoryReportTab`、`HistoryReportViewModel` 与 `Assets/HistoryReport/`
  四个资源文件（`report.html` / `report.css` / `report.js` / `chart.js`，共约 283 KB）
- 跨页落点 `NavigateTo("WebAccess", "history")` 改为滚动到页面底部的历史区块并加载它
  （分析页的 AI 行动项与工具栏按钮继续可用）

### 说明：未重复呈现的内容

原报告的「每日访问趋势 / 24 小时分布 / TOP10 网站」与统计页已有的卡片
**内容重复**（且后者按会话统计、前者按历史库聚合，口径本就不同），
故不再重复呈现，只保留独有的域名明细。

### 验证

- Release 构建 0 错误；单元测试 154/154 通过
- publish 后部署 `D:\Tool\PChabit`，旧页 `.xbf` 与 HistoryReport 资源已自动清理
- 截图确认：网页访问页已无页签、历史区块为原生卡片且与上方风格一致、数据正常渲染
- 新增 ERR/FTL = 0

---

## [3.35.0] - 2026-10-10

### 变更：4 处「每小时分布」图表合并为共享控件

`AppStatsTab` 每小时活动、`WebStatsTab` 每小时活动分布、
`KeyboardDetailsTab` 每小时按键分布 / 每小时鼠标统计——
原先每处都自写一遍「标签 + ProgressBar + 数值」三列 Grid，
列宽、间距、数值格式各不相同，改一次样式要改四处。

新增 `Controls/HourlyBarChart`：
- 数据源统一为 `HourlyBarRow`（Label / Value / Display），各 ViewModel 负责投影，
  控件本身只管呈现
- 行在代码里生成而非 DataTemplate——行内 `ProgressBar.Maximum` 依赖整组数据，
  只能整表重建，模板化反而更绕
- 顺带修两处显示缺陷：`WebStatsTab` 原 `ProgressBar Maximum` 硬编码 50
  （超过 50 次访问的小时会被截断）、`KeyboardDetailsTab` 原硬编码 1000；
  现统一按数据自动取值并留 10% 余量

### 变更：硬件监控移出侧栏，入口改到「设置 › 监控」

硬件监控是辅助信息，与「应用使用 / 网页访问」这条统计主线无关，
此前却与它们并列在侧栏同一级。

- 侧栏移除「系统」分组与「硬件监控」项（侧栏 10 项 → 9 项，四组）
- `SettingsPage` 的「监控设置」分区新增「打开硬件监控 →」按钮
- **导航 key `"Monitor"` 保留**，桌面挂件双击（`App.DesktopWidget.cs:124`）照旧可用
- 硬件页补一行副标题，说明采集范围

### 验证

- Release 构建 0 错误；单元测试 154/154 通过
- publish 后部署 `D:\Tool\PChabit`
- 截图确认：侧栏已无「硬件监控」；设置页「监控」分区入口可进入硬件页；
  4 处图表数据正常渲染，新增 ERR/FTL = 0

---

## [3.34.0] - 2026-10-10

### 变更：侧栏收口 —— 应用统计与键鼠详情合并为「应用使用」

侧栏「使用」组下的「应用统计」与「键鼠详情」两项内容同源（都是应用使用行为统计），
拆成两页导致来回切换。合并为单页双页签：**应用排行 / 键鼠详情**。

- `KeyboardDetailsPage` 按陷阱 29 转为 `KeyboardDetailsTab`（UserControl）：
  根节点 / `x:Class` / `</Page>` 同步改，去掉 `OnNavigatedTo`，
  改 `Loaded` + 供宿主调用的 `EnsureLoadedAsync()`
- 新建宿主 `AppUsagePage`（Pivot 双页签），删除转发壳页 `AppStatsPage`
- **旧导航 key 保留为别名**：`AppStats` / `KeyboardDetails` 均注册到 `AppUsagePage`，
  `NavigationService._aliasPivotTags` 自动补 `AppUsageNavArgs`，
  使既有调用（仪表盘「查看全部 →」）落到「应用排行」页签，行为不变
- 侧栏 11 项 → 10 项

### 修正：原方案「删掉 WebAccessPage / CategoryManagementPage 壳页」判断有误

方案文档曾把这两页列为「转发壳页」，实施前逐项核实发现**不成立**：

- `WebAccessPage.OnNavigatedTo` 承载 3.31.1 刚修好的跨页落点
  （分析页 AI 行动项 → `NavigateTo("WebAccess", "history")` 直接打开「网页浏览历史」页签）。
  删掉该页会导致此行为回归
- `CategoryManagementPage` 本身就是分类管理的正常宿主页（标题栏 + 双页签），无壳可上提

「转发壳页」这一判断只对 `AppStatsPage` 成立（其内容除标题栏外仅一行 `<views:AppStatsTab/>`），
该页已随本次合并删除。方案文档已同步更正。

### 验证

- Release 构建 0 错误；单元测试 154/154 通过
- publish 后部署 `D:\Tool\PChabit`，旧页 `.xbf` 由部署脚本自动清理
- uiautomation 实测：侧栏显示「应用使用」，两页签均可加载并有数据，
  旧 key 别名跳转落点正确，新增 ERR/FTL = 0

---

## [3.33.0] - 2026-10-10

### 变更：合并两套分类管理 CRUD（重复实现从 4 处降为 0）

3.30.0 把分类管理挪到独立页时，应用分类与网站分类保留了两套逐行对称的实现。
改一处 UI 行为（例如分类名校验规则）必须记得改另一处，改漏不会报错、只会静默不一致。

**通用 CRUD 上移**（新增 `CategoryCrudViewModelBase<TCategory, TMapping>`）：

- `CategoryManagementViewModel` 328 行 → 89 行
- `WebsiteCategoryManagementViewModel` 325 行 → 60 行
- Initialize / SelectCategory / ApplyFilter / Add / Update / Delete 全部共用一份实现
- 线程约定统一：DB 查询在线程池、`ObservableCollection` 填充在UI 线程（陷阱 5.6）

**编辑对话框合并**（两份 XAML 相似度 98.9%，仅差一个 `PrimaryButtonClick`）：

- 删除 `WebsiteCategoryEditDialog.xaml`（141 行），改为纯代码类继承 `CategoryEditDialog`
- 统一走应用版的 `Closing` + `GetDeferral()` 写法（陷阱 2 推荐），
  网站版原先用的 `PrimaryButtonClick` 无法做异步验证
- 新增 `CategoryEditDialogViewModelBase`，校验与物化逻辑一份实现

**实体接口化**：新增 `ICategoryEntity` / `IMappingEntity`（Core 层），
两组同构实体实现之，使泛型基类无需反射即可访问公共字段。

### 修复：网站分类种子数据存在第二份副本

`WebsiteCategoryManagementViewModel.InitializeAsync` 里内联了一整份默认网站分类
与域名映射数据（58 行），与3.32.0 刚移到 `WebsiteCategoryService` 的那份完全重复。
两份各自维护，改一处就会导致「首次运行种子」与「补齐种子」结果不一致。
本次删除内联副本，种子补齐统一由 `DatabaseInitializer` 在启动期完成。

### 技术要点：ContentDialog 的 `x:Bind` 必须显式声明 `x:DataType`

合并后 `CategoryEditDialog` 的 `ViewModel` 属性类型是基类，但 XAML 里省略 `x:DataType` 时，
编译器会把 `x:Bind` 推断成属性的**实际类型**（`CategoryEditDialogViewModel`），
生成 `Update_ViewModel(CategoryEditDialogViewModel obj, ...)`，导致派生类场景报
CS1503 类型不匹配。补上 `x:DataType="vm:CategoryEditDialogViewModelBase"` 后正常。

### 验证

- Release 构建 0 错误；单元测试 154/154 通过
- publish 后部署 `D:\Tool\PChabit`，uiautomation 实测分类管理页两个页签正常
- 新增 ERR/FTL = 0

---

## [3.32.0] - 2026-10-10

### 修复：分类种子逻辑依赖死代码（新装机器分类管理页会空）

排查 3.30.0 遗留的重复代码时发现：`CategoryService.EnsureCatalogSeededAsync`
（补齐默认分类与进程映射）的**唯一调用点**位于 `SettingsViewModel` 的死代码里
——3.30.0 把分类管理挪到独立页时删了设置页的 UI，但没删背后的代码路径。

后果：用户若从未打开过设置页，新装机器进入「分类管理」会看到空列表，
应用/网页统计页的分类归属也会全部落到「未分类」。

修复：把种子补齐移到 `DatabaseInitializer.SeedDefaultDataAsync`，
即数据库初始化阶段执行，不再依赖用户是否打开过设置页。
网站分类同步抽出静态 `WebsiteCategoryService.EnsureCatalogSeededAsync`，
补齐原先只在设置页路径下才走的网站分类种子。

### 变更：删除约 640 行死代码

3.30.0 的减法版本删掉了设置页的分类管理 UI，但代码全部留着且仍在跑。
本次按「全库零引用」标准逐项核实后删除：

| 文件 / 成员 | 行数 | 说明 |
|---|---|---|
| `SettingsViewModel.Categories.cs`（整文件） | 435 | `LoadCategoriesAsync` / `SaveCategoryFromDialogAsync` / `ExportCategories` / `ImportCategories` 等 12 个方法全库零调用 |
| `SettingsViewModel.Mappings.cs`（整文件） | 53 | `LoadCategoryMappings` 硬编码 6 条假数据（`code.exe`→开发 等），是假数据源 |
| `MainViewModel.cs`（整文件） | 86 | `ShellPage` 只读 XAML 写死的 MenuItems，此VM 零引用 |
| `AnalyticsViewModel` 死成员 | 60 | `WeeklyData` / `Trends` / `Patterns` / `InsightsLegacy` / `TotalActiveTime` / `AverageDailyTime` / `AverageProductivity` / `TotalKeyPresses` / `TotalMouseClicks` / `TotalWebPages` 及4 个配套类 |

连带收益：`SettingsViewModel.InitializeAsync` 不再每次启动跑 2 次无人观看的分类
DB 查询，构造函数也去掉了一次同步的假映射填充。

### 变更：`SettingsViewModel` 构造函数去掉 `IDbContextFactory` 参数

分类查询移除后该依赖不再使用。已核实全库仅经 DI 构造、无手工 `new`、
测试项目未引用，移除不影响任何调用方。

### 验证

- Release 构建 0 错误 19 警告
-单元测试 154/154 通过
- publish 产物已部署 `D:\Tool\PChabit`，启动日志无新增 ERR

---

## [3.31.1] - 2026-10-10

### 变更：「历史」页签改名并转移到网页访问

- 分析页原「历史」页签标题改为**网页浏览历史**，并从分析页移除
- 网页访问页改为双页签：**访问统计** / **网页浏览历史**
- 分析页两个入口（总览工具栏按钮、网页摘要卡片按钮"查看网页浏览历史 →"）
  及 AI 行动项点击后，整页导航到网页访问页并自动落到历史页签
- 同步更名：HistoryReportTab 标题栏、报告前端 report.html 的 title 与大标题

### 验证

- Release 构建 0 错误；已部署 `D:\Tool\PChabit`（465 文件，3.31.1.0）
- UI 实拍验证：网页访问页双页签均可正常加载（历史库 25,103 条），
  分析页入口跳转后正确选中「网页浏览历史」页签

## [3.31.0] - 2026-10-10

### 修复：全项目分类口径统一（消除"同一数据、两套逻辑"）

时间线里的应用分类与应用统计页不同步：时间线直接读会话快照 `AppSession.Category`，
绕过了用户可编辑的 `ProgramCategoryMappings` 映射表；用户在分类管理里改了分类，
应用统计页变了、时间线不变。全面排查后共发现 **8 处**重复逻辑，本版全部收敛到
两个权威：应用分类 → `AppStatsEngine`，网站分类 → `IWebsiteCategoryService`。

应用分类（进程 → 分类）5 处：

- **时间线**：会话分类/小时条形配色/近 7 日高效占比全部改用
  `AppStatsEngine.ResolveCategory`（映射优先 → 会话快照 → 未分类），删除自带的
  13 色硬编码色板与私有"高效分类"表；高效判断改用 `AnalyticsEngine.IsProductiveCategory`
- **桑基图**：`GetCategory` 硬编码关键词猜测改为引擎解析（新增 DbFactory 注入）
- **分析引擎**：生产力占比 / 专注段 / 分类构成在 BuildAsync 中先按映射表重解析
  会话分类，Compute 与各测试重载行为不变
- **键鼠详情（鼠标程序统计）**：自查映射精确匹配改为
  `AppStatsEngine.BuildCategoryMap + ResolveCategory`
- **仪表盘**：删除与引擎重复的 `ResolveCategorySafely` 副本，直接调引擎

网站分类（域名 → 分类）3 处：

- **网页访问页**：删除复制的域名匹配 / 关键词兜底 / 颜色表，改用服务一次性
  加载映射与分类颜色
- **仪表盘卡片详情弹窗**：此前纯硬编码兜底、**连用户自定义映射都不查**，
  改用 `ResolveForDomainSync`
- **网页会话落库**：删除复制的 DomainMatches / FallbackCategoryName，
  改用服务新增的 `ResolveForDomainSync`

服务接口新增：`GetDefaultCategoryName`、`ResolveForDomainSync`（返回
名称/颜色/分类Id/来源），`DomainMatches` 改为公开静态方法。

### 验证

- 154/154 单元测试通过；Release 构建 0 错误
- 数据库实证：近 3 天存在 10+ 个快照分类与映射表分叉的进程
  （如 Doubao 快照=AI 助手、映射=浏览；QwenWorkCN 快照=其他、映射=AI 助手），
  修复后这些页面统一显示映射表结果
- UI 实拍时间线：PChabit/资源管理器显示「系统工具」、红果短剧显示「娱乐」，
  标签与配色均取自映射表
- 已部署 `D:\Tool\PChabit`（465 文件，3.31.0.0，自包含校验通过），
  启动日志无新增错误

## [3.30.3] - 2026-10-10

### 改善：下载网速独占任务栏左右分栏的上下两行

3.30.2 把下载速度提到主行首位，但它仍和 CPU/内存/GPU 排在同一行。这一版给它
**独立的左侧区块，独占上下两行**，与右侧指标之间用竖分隔线切开。

- **左侧区块占满两行高度**：
  - 上行：当前下载速率 `↓12.3M`，字号提到 21px（全窗口最大），速率分级配色不变
  - 下行：今日累计下载量 `今 1.24G`，14px，取自 `NetworkTrafficSamples` 分钟样本
    当天求和（60 秒缓存，与「今日使用时长」同一套缓存模式）
- **竖分隔线**：区块文字右侧 9px 处画 1px 竖线（DIM 色），贯穿两行，视觉上彻底分开
- **右侧保持原两行**：上行 CPU / 内存 / GPU / 显存，下行 上传 / 盘 / C·G 温度 / 今日使用
- **控件接口调整**：`TaskbarWidget.UpdateContent` 参数由 `(row1, row2)` 改为
  `TaskbarLayoutItem(BlockRow1, BlockRow2, Row1, Row2)`；`MeasureAndResize` 分别测量
  区块宽与右侧宽，分隔线宽度计入总宽；关闭网速开关时自动退回纯两行布局
- 开关不变，仍是设置 → 任务栏 → 「网速」（`TaskbarShowNet`）

### 修复

- EF 表达式树里不能用 string 的 `>=` / `<`（`CS0019`）：查询今日下载量改用
  `Timestamp.StartsWith("yyyy-MM-dd")` 前缀匹配

### 杂项

- 版本号 3.30.2 → 3.30.3

## [3.30.2] - 2026-10-10

### 改善：任务栏下载速度显示（更醒目）

任务栏小窗原本把网速 `↓xxK ↑xxK` 塞在次行、用小号弱化字体，下载中不容易注意到。

- **下载速度提升到主行首位**：从次行挪到主行最左侧（视线最先到达的位置），
  并用比常规数值更大的字号（17px vs 15px），其后再留 20px 分组间距与 CPU/内存/GPU 隔开
- **速率分级配色**：空闲灰（<100KB/s）→ 活跃蓝（≥100KB/s，#185FA5 / #85B7EB）→
  高速青（≥1MB/s，#0F6E56 / #5DCAA5）。用冷色表示"正在传输"，与负载/温度的黄红警告语义区分开
- **格式收窄**：`↓340K` / `↓12.3M` / `↓123M`（高速省掉小数），保证与后面 4 项
  百分比同处一行时不会把小窗撑太宽；不足 1KB/s 显示 `↓0`，避免空闲时数字抖动
- **上传留在次行**：`↑xxK` 仍用次行小字，不抢下载的位置
- 开关不变，仍是设置 → 任务栏 → 「网速」（`TaskbarShowNet`）

### 杂项

- 版本号 3.30.1 → 3.30.2

## [3.30.1] - 2026-10-10

### 修复：点击设置页崩溃（3.30.0 引入的回归）

- **现象**：侧栏点「设置」直接崩溃，日志为 `XamlParseException: XAML parsing failed`，
  内层是 `NullReferenceException`（`ApplySection` 中访问分区控件）
- **根因**：设置页分区导航的 `ListViewItem` 上写了 `IsSelected="True"`。XAML 解析到这一项时
  **立即**触发 `SelectionChanged`，而右侧 9 个分区 `Border` 定义在 ListView **之后**、
  此刻尚未创建，`GeneralSection` 等 x:Name 字段还是 `null`，设置 `Visibility` 时空引用，
  异常冒泡把整个 `InitializeComponent` 打成 XAML 解析失败
- **修复**：
  1. XAML 去掉 `IsSelected="True"`，选中项改在 `InitializeComponent` 之后用
     `SectionList.SelectedIndex = 0` 设置
  2. `ApplySection` 的 `Show` 改为可空参数并加 null 判断，防御任何提前触发
  3. `SectionList_SelectionChanged` 改用 `sender` 而不是 `SectionList` 字段，
     避免解析期字段未赋值
  4. 同类隐患一并修：硬件监控页 `MainPivot_SelectionChanged` 也改用 `sender`
     （Pivot 在解析期同样可能触发 SelectionChanged）
- **验证**：`dotnet build` 0 错误，启动后点设置页不再崩溃

### 杂项

- 版本号 3.30.0 → 3.30.1

## [3.30.0] - 2026-10-10

### 减法：合并重复入口与界面，让导航更清晰

这一版只做减法，不加新功能。目标是让「同一件事只有一个入口」。

#### 1. 删除两个无人使用的死页面

- **热力图页 `HeatmapPage`**（447 行 XAML）：只在导航服务里注册过，侧栏从未有入口；功能早已被分析页「节奏」页签的「近 14 天活跃热力」覆盖。连带删除 `HeatmapViewModel` 与只被它使用的 `HeatmapAggregator`
- **洞察页 `InsightsPage`**：同样无入口，功能被分析页的 AI 深度解读覆盖。连带删除 `InsightsViewModel`

#### 2. 网络流量并入硬件监控

- 硬件监控页新增「实时 / 流量」两个页签，原网络流量页改为页签控件 `NetworkTrafficTab` 嵌入
- 侧栏去掉「网络流量」入口。切到「流量」页签时才查库，进页面不再无谓加载

#### 3. 历史分析并入分析页

- 分析页新增第 6 个页签「历史」，原历史分析页改为 `HistoryReportTab` 嵌入
- 侧栏去掉「历史分析」入口。此前「历史分析」有三个入口（侧栏 + 分析页顶部按钮 + 分析页卡片链接），现在三个都收敛为切换页签

#### 4. 分类管理合并为一处

- 新增「分类管理」页：顶部切换「应用分类 / 网站分类」，把原先分散在「应用统计 - 分类管理」和「网页访问 - 网站分类」的两套维护界面收拢到同一入口
- 应用统计页、网页访问页各自退化为单页（只留统计），顶部加「分类管理 →」跳转

#### 5. 仪表盘只留 Top 5

- 「应用使用排行」「网站访问排行」改名为「应用使用 Top 5」「网站访问 Top 5」，只显示前 5 条，底部加「查看全部 →」跳到对应页面
- 顺带省掉了全量应用图标的加载开销

#### 6. 设置页加左侧分区导航

- 设置项按 常规 / 监控 / 界面 / 便签 / 数据 / AI / 关于 分七组，左侧点选、右侧只显示当前分组
- 不删任何设置项，只解决「1025 行一页长滚、找个开关要滚很久」的问题

#### 7. 侧栏导航分组

- 10 个平铺入口按使用动线分为 概览 / 使用 / 系统 / 洞察 / 工具 五组，各带分组标题

### 杂项

- 版本号 3.29.0 → 3.30.0

## [3.29.0] - 2026-10-03

### 新增：四模块联动升级（悬浮插件 / 搜索意图 / 时段画像 / 网页分类）

- **① 桌面悬浮插件「当前活动」行**：悬浮窗新增「当前」行（设置-桌面悬浮插件-当前活动，默认开启），实时显示前台进程名及其活动分类（分类口径与网络流量/统计页 AppStatsEngine 一致，映射 60s 缓存刷新）；未映射进程仅显示进程名。`ISettingsService.DesktopWidgetShowCurrentApp` 全链路（接口/持久化/设置页开关）
- **② 搜索意图聚合（隐私安全）**：`DailySummary` 新增 `SearchCount`（可空列，迁移幂等）；浏览器扩展上报搜索时仅累加计数、**不存关键词原文**（与 `SearchQueries` 不落库的既有隐私口径一致）；当日计数每小时回写 + 退出时 flush（`UpsertTodaySearchCountCoreAsync` 增量 upsert，保留行内其他字段，不被每日重聚合覆盖）；AI 指标包/规则摘要新增「搜索 N 次」维度
- **③ 时段画像：小时级四维对齐**：历史分析页「24 小时活跃分布」卡升级为「24 小时四维分布」，新增两行小时热力：网页有效浏览（分钟，绿→靛蓝 #6366F1）、下行流量（MB，琥珀 #F59E0B），与既有活跃行逐小时对齐（期内所有天同小时聚合）；AI 指标包同步携带 `hourWebMinutes` / `hourNetDownMb` 24 桶序列，供周报描述「典型一天」
- **④ 网页分类 × 应用分类统一叙事**：历史分析页新增「网页分类构成」卡（WebSessions.CategoryName 口径，与「分类构成（应用）」卡同版式并排对比）；AI 指标包新增 `webCategories`（分类/分钟/占比），规则摘要新增「网页分类构成」节；`AnalyticsPeriodReport` 扩展 `HourWebMinutes / HourNetDownMb / WebCategoryShares`（record 位置参数追加在末尾，无破坏性变更），`AnalyticsExtraMetrics` 扩展 `SearchCount`

### 杂项
- 版本号 3.28.0 → 3.29.0（纯数字递增）

## [3.28.0] - 2026-10-03

### 新增：网页访问 / 网络流量 / 历史分析三模块联动
- **P0-① 流量按应用分类聚合（网络流量页）**：新增「分类流量占比」卡片，进程流量按 AppStatsEngine 统一分类口径（设置-应用分类自定义映射）聚合到 工作/娱乐/工具 等分类，展示各分类上下行、占比与色块；未映射进程归入「未分类」
- **P0-② AI/历史跨域叙事（深度解读/周报）**：AI 指标包新增上行/下行流量（GB）维度（数据源 DailySummary.NetBytesUp/Down，由网络流量落库服务日汇总回写），深度解读、周报 prompt 自动携带流量信息；规则引擎「多源摘要」同步新增流量行。`AnalyticsExtraMetrics` 扩展 `NetBytesUp/NetBytesDown/HasNetData`（record 位置参数追加在末尾，无破坏性变更）
- **P1-③ 网页流量归因（估算，网页访问页）**：新增「网页流量归因（估算）」卡片，浏览器进程（Chrome/Edge/Firefox）下行流量按「同日期同小时各域名有效浏览时长」权重加权拆分到域名，标注估算口径；某小时无浏览器流量或网页会话时不参与
- 明确不做：进程→连接→URL 精确映射（需 ETW/驱动级采集，与「纯用户态稳定优先」冲突）

### 杂项
- 版本号 3.27.0 → 3.28.0（纯数字递增）

## [3.27.0] - 2026-10-03

### 优化：桌面悬浮插件网速条满量程适配
- **下载进度条固定 100M/s 满量程**（用户宽带下载最大速度）：100M/s 满格、50M/s 半格、1M/s 约 1%，直观反映绝对速率；超速时条满格、速率文本照实显示（如 108M/s）。此前为自适应峰值（条长相对近期峰值），高速时长期满格、低速时也接近满格，无法体现绝对速率
- **上行进度条保持自适应峰值**（上行速率范围差异大，自适应保证条长可读性），下载/上行两行逻辑拆分（`BuildDownloadRow` / `BuildNetRow`）

### 修复
- 便签启动清理竞态（P0）：`App.StickyNotes.cs` 的启动清理与 WebDAV 同步未等待数据库迁移完成即读写 `StickyNotes` 表，首启可能抛 `no such table: StickyNotes`。改为 `Task.WhenAny(_dbInitCompleted.Task, 15s 超时)` 兜底（与 `StartBackupService` 同一模式）
- `CHANGELOG.md` 双重编码乱码：标题、`[Unreleased]` 与 3.24.0 至 3.22.0 区段已修复；3.21.1 及更早仍有残留（见下方「已知问题」）

### 杂项
- 清理根目录遗留的 `PChabit.exe_20261002_095322.nettrace` 诊断转储，并将 `*.nettrace` 加入 `.gitignore`（该类文件会在项目根反复生成）

### 已知问题
- `CHANGELOG.md` 中 **3.21.1 及更早**（约 100 个版本、309 行）仍存双重编码残留。根因是历史上的 GBK 与 UTF-8 转换经过有损环节：部分字节被替换为 `?`，私用区字符（U+E000–U+F8FF）混入，字节信息已丢失，**机器无法逆转**（`gb18030` 往返后仍解不出合法 UTF-8）。
  - 已修复范围：文件标题、`[Unreleased]`、3.24.0 至 3.22.0 全部区段（关于页「更新日志」当前展示范围已完全可读）
  - 剩余部分需按语义重写或从外部备份恢复，不做猜测性改写

## [3.26.2] - 2026-10-03

### 修复：网页会话批量落库 UNIQUE 约束冲突（长期存在的静默数据丢失）
- **现象**：日志持续报 `SQLite Error 19: 'UNIQUE constraint failed: WebSessions.Id'`（实测单日 40+ 次），
  报在 `批量处理数据操作失败` —— 意味着**整批（最多 50 条）数据全部未落库**
- **根因**：`EnqueueSaveWebSession` 无条件 `dbContext.WebSessions.Add(session)`，
  而周期落库 `SaveActiveWebSessionsPeriodicallyAsync` 已用 `FindAsync` 判断并正确处理更新。
  同一会话（同一 Id）先入队 Add、随后又被周期落库 Add；两条操作落在同一批次、
  共用同一个 `DbContext` 时，EF 在 `SaveChanges` 对同一 Id 执行两次 INSERT → UNIQUE 冲突
- **修复**：
  - `EnqueueSaveWebSession` 改为「先查后插/更新」，更新分支复用与周期落库相同的字段集合
  - `ProcessDataAsync` 整批失败时**改为逐条隔离重试**（每条独立 `DbContext`）。
    原实现下一条脏数据会让整批 50 条一起丢；现在最多损失一条
- 效果：消除错误日志，同时不再出现「整批数据静默丢失」

## [3.26.1] - 2026-10-03

### 修复：仪表盘与应用统计页的应用分类不同步（3.26.0 引入的回归）
- **现象**：同一进程在「仪表盘」与应用统计页显示不同分类。实测 `MuMuNxDevice.exe` 仪表盘显示「其他」/应用统计显示「娱乐」；`QwenWorkCN.exe` 仪表盘「其他」/应用统计「AI 助手」
- **根因**：3.25.0（阶段 1）把仪表盘的分类改为 `AppCategoryResolver` 实时硬编码映射，
  **绕过了 `ProgramCategoryMappings`（198 条有效映射，用户可在设置中自定义）**。
  应用统计页走 `AppStatsEngine.ResolveCategory`：映射表优先 → 回落 `AppSession.Category`。
  两页因此走了两套口径
- **修复**：仪表盘改为与应用统计页**同源**——
  - 新增 `LoadCategoryMapAsync` 读同一张 `ProgramCategories` + `ProgramCategoryMappings`，
    直接复用 `AppStatsEngine.BuildCategoryMap` / `NormalizeProcessName` / `DefaultColorFor`
  - 解析优先级与 `AppStatsEngine.ResolveCategory` 完全一致：映射表 → `AppSession.Category` → 「未分类」
  - 回落值改为传入该进程真实的 `AppSession.Category`（此前传 null，会丢失历史数据的分类）
- **分类配色也一并统一**：分类分布与 TopApps 分类标签的颜色取自 `ProgramCategories.Color`，
  不再使用硬编码色板（`#512BD4` 等），因此两页配色也一致
- 移除 3.25.0 引入的 `AppCategoryResolver` DI 注册（现已无用）

### 验证
- 编译 0 错误、154/154 全量测试通过
- 以生产数据库复刻两页解析逻辑比对：`MuMuNxDevice.exe` → 娱乐、`QwenWorkCN.exe` → AI 助手、
  `explorer.exe`/`cmd.exe` → 系统工具、`msedge.exe` → 浏览，**与应用统计页完全一致**
- 全库核查：`AppCategoryResolver` 现仅在采集侧（`AppMonitor` 写入 `AppSession.Category`）使用，
  这是它应在的唯一位置

## [3.26.0] - 2026-10-03

仪表盘升级阶段 2–6（布局 / 图表 / AI / 实时化 / 打磨）。阶段 1 见 3.25.0。

### 新增：顶部状态条
- **今日效率评分**（大数字 + 等级文本），来自 `EfficiencyCalculator` 四维加权，与「分析」页同源
- **相对个人基线的偏离**（如「高于平时 12%」）；样本不足时显示「基线样本积累中」而非给出不可信的百分比
- **专注时长 / 应用切换 / 活跃时段**三项，取自 `DailySummary` 扩展字段；字段未产出时显示「--」而非 0（「真的是 0」与「还没数据」是两件事）
- **数据截止时刻**，避免把累计值误读为此刻的实时值

### 新增：环比与实时
- 四张统计卡各带**环比昨日同时段**的涨跌标记（▲/▼/●）。截断到「昨日的当前时刻」——直接用昨日全天数据会在午后打开时造成「大幅下降」的假跌
- **「当前活动」条**：显示正在使用的应用（图标 + 开始时刻），带「实时」标识
- **30 秒自动刷新**，进入页面启动、离开即停（对齐 3.24.0「进页面才 Start、离开即 Stop」的省电思路）。选 30 秒而非 1 秒：3.24.0 已证明高频采样代价明显
- 图表数据变化时局部推送，不重建页面，因此滚动位置与悬停态保持不变

### 新增：今日速览（AI 或规则）
- 调 `IAnalyticsAiService.ChatFastAsync` 生成一句话状态概括 + 一句可执行建议
- **未配置 AI 时自动降级为规则摘要**（最常使用应用 + 效率分 + 专注 + 相对平时），不显示空卡片
- 摘要来源以标签明示（AI / 规则），让用户知道这段话怎么来的
- 附**习惯画像摘要**（时型判定 + 今日活跃时段）

### 改进：图表全面替换
- **每小时活动改用 WebView2 柱状图**，替换原先「竖向 ProgressBar 旋转 -90 度」的取巧做法。24 根圆角矩形并排的锯齿问题消失
- **未到的时段画灰色占位**，与「真的零活跃」明确区分（此前未来小时根本不画，横轴不完整）
- 按真实分钟数绘制（原先是百分比），Y 轴含义改为「分」
- **主题自适应**：随应用深浅色切换图表的画布、网格线、坐标轴与文字颜色（既有 `hardware-trend.html` 硬编码深色）
- WebView2 初始化失败时显示降级提示，**数据加载与图表初始化解耦**（陷阱 #13），数字照常呈现
- 页面离开时释放 WebView2

### 改进：交互与视觉
- **「今日活动时间」卡片补上点击**（跳转时间线页）——此前四张卡只有三张可点，而这张恰是最该深挖的
- **键盘可达**：四张卡均可 Tab 聚焦、Enter/空格触发（此前只能用鼠标）
- **悬停效果改用主题资源 + `HoverableCardStyle`**，替换代码-behind 硬写 `Application.Current.Resources[...]`——后者绕过主题系统，深浅色切换时闪白
- **补上分类分布可视化**：`CategoryDistribution` 此前在 ViewModel 算好了百分比与配色，但 XAML 从未绑定（死代码），现以进度条形式呈现
- TopApps 行内补分类标签（来自 `AppCategoryResolver`）
- 新增空态：无任何今日数据时显示引导文案而非「0小时 0分钟」

### 修复
- **定时器重入竞态**（陷阱 27）：`DbSafeViewModel.LoadDataAsync` 的 `if (IsLoading) return` 非原子，
  两个 tick 可能先后通过检查后并发查询。改用 `Interlocked.Exchange` 做原子防重入
- 键盘路径无法构造 `TappedRoutedEventArgs`，故将四张卡的行为拆为无参方法，鼠标与键盘共用
- 派生文本属性（`FocusMinutesText` 等）为组合计算，`x:Bind OneWay` 不会自动刷新，改为手动 `OnPropertyChanged` 抬升

### 验证
- 编译 0 错误；**154/154 全量测试通过**
- 全程两次撞上陷阱 #21（1 个 C# 错误引发 6 个 XAML 假错误含 WMC9999），均按「先修 C# 再看 XAML」解决
- 启动日志确认：效率评分与聚合表读取正常（`SummaryMetricsVersion=2`）、实时刷新已启动（30 秒）、
  未配置/异常时 AI 速览自动降级为规则摘要且不阻塞页面
- **AI 速览实测降级原因**：所用 DeepSeek 模型返回 `content:""` 但带 `reasoning_content`（思考模型特征），
  属既有的 3.22.2 场景。降级路径按设计接住，用户看到的是规则摘要而非错误

## [3.25.0] - 2026-10-03

### 重构：仪表盘数据层口径收口（阶段 1/6）
- **改读日聚合表的扩展指标**：从 `DailySummaries` 取 `FocusMinutesV2`（专注）、`AppSwitches`（应用切换）、`FirstActiveTime`/`LastActiveTime`（首末活跃），这些字段此前一个都没用上
- **效率评分改用 `EfficiencyCalculator`**：删除仪表盘自建的 `ProductivityScore` 与硬编码 `IsProductiveCategory`（只认「开发/开发工具/办公/办公软件」4 个字符串），改用项目统一的四维加权评分，与「分析」页同源同值
- **应用分类改用 `AppCategoryResolver`**：删除 `GetAppCategory` 的 `Contains("code")` 关键词猜测。现按 12 类归类（开发/AI 助手/浏览/办公/沟通/媒体创作/设计/云盘下载/娱乐/系统工具/生产力/其他），且分组改为 `(ProcessName, ExecutablePath)` 复合键，同名不同路径的应用不再被错误合并
- **接入个人基线**：用 `HabitDayLoader`（近 28 天）+ `PersonalBaselineBuilder` 计算活跃时长相对 P50 的偏离度；样本不足 3 天时返回 `null` 而非给出不可信的百分比
- **分类分布与 TopApps 分类同源**：原先两套分类口径不一致（一个用 `s.Category` 字符串、一个用关键词匹配），现统一走 Resolver
- 评分与基线计算各自 try-catch 降级（异常时页面不空白），日志记录失败原因

### 修复
- **测试项目自 3.23.0 起无法编译**：`AiEndpointResolveTests.FakeSettings` 未实现 `ISettingsService` 新增的 24 个成员（`DesktopWidget*` 16 个 + `StickyNotes*` 8 个），补齐后 **154/154 全量测试通过**。此前一直无人运行测试，故问题未暴露
- `AppCategoryResolver` 在 App 层 DI 缺失（App 未调用基础设施的 `AddTaiInfrastructure`），显式补 `AddSingleton`（陷阱 #3 形态）

### 移除
- `AppUsageItem.Percentage` 字段：数据源按分类而非按应用计算，占比已无意义，且 XAML 本就未绑定

## [3.24.0] - 2026-10-02

### 新增：便签功能 P0（全局热键快速录入 → SQLite → 便签管理页 → WebDAV 同步）
- **全局快捷键**：`Ctrl+Alt+N` 呼出快速录入窗（420×260 无边框置顶小便签，定位鼠标所在屏幕），`Ctrl+Enter` 保存、`Esc` 隐藏留草稿、失焦 1.5s 自动处理（空白直接隐藏/有内容按设置保存）；`Ctrl+Alt+B` 打开主窗口便签页；热键可在设置中自定义，注册失败在设置页红字提示
- **快速录入窗**：纸质便签外观 5 色切换（黄/粉/蓝/绿/灰），内容 500ms debounce 暂存 `%LOCALAPPDATA%\PChabit\note-draft.json`，崩溃/重启不丢字、再次呼出自动恢复；单例复用（Hide 而非 Close，Alt+F4 拦截为隐藏）
- **便签管理页**（导航新增「便签」）：全部/置顶/颜色筛选、关键词搜索、卡片列表（置顶/改色/归档/还原/删除）、编辑对话框（Ctrl+Enter 保存）、回收站（还原/彻底删除/清空回收站带确认）
- **存储**：Core 层 `StickyNote` 实体 + `IStickyNoteService`，Infrastructure 层 SQLite `StickyNotes` 表（增量 CREATE TABLE IF NOT EXISTS）；回收站 30 天保留（可配置），启动清理过期记录
- **WebDAV 同步**：复用现有 WebDAV 配置，JSON 墓碑删除 + 时间戳 LWW 合并；双开关（功能总开关 + 同步开关），未配置静默降级；启动后 30s 首次、之后 15 分钟周期、保存后 30s debounce 触发
- **入口**：托盘菜单新增「新建便签」「便签列表」；设置页新增「便签」卡片（总开关/两个热键/失焦保存/回收站保留天数/同步开关+立即同步+上次同步时间）

### 优化：常驻 CPU 占用降低约 66%
- 硬件监控采样周期 2000ms → 5000ms；GPU 性能计数器上限 160 且只保留 3D/VideoDecode 引擎；计数器 Exists 结果缓存（排查数据：GPU 644 个实例含 168 Copy/128 3D，NextValue 单次 370ms、Exists 460ms，而进程枚举仅 15-19ms）
- 硬件监控改为进入页面才 Start、离开即 Stop（移除启动时常驻 ProcessResourceMonitor.Start()）
- 桌面悬浮窗内容签名 `_contentSig` 无变化跳过重绘
- 实测空闲 CPU 从约 1.62%（整机）降至 0.43~0.55%（12 逻辑核）

## [3.23.2] - 2026-10-02

### 改进：悬浮窗文字阴影 + 渲染光滑度（渲染架构升级）
- **渲染管线从 LWA_COLORKEY 色键透明升级为 UpdateLayeredWindow per-pixel alpha**（WS_EX_LAYERED + 32bpp 顶向下 PARGB DIB + GDI+）：色键方案下文字抗锯齿边缘是"文字色与色键底色混合"的不透明像素，在非色键色壁纸上带杂边，且无法表现半透明阴影
- **文字增加投影**：所有文字（标题/标签/数值）统一绘制 1px 右下偏移的半透明黑色阴影；浅色主题不透明度 31%、深色主题 66%（白字在浅色花壁纸上靠重影勾边），花哨壁纸下可读性显著提升
- **文字更光滑**：GDI 灰度抗锯齿改为 GDI+ `AntiAliasGridFit`（现代光栅化+网格微调），边缘像素写入真实 alpha 通道，与任意壁纸正确合成，无色键杂边/无 ClearType 彩色毛边（分层窗口不用 ClearType，与 WPF 透明窗口策略一致）
- 药丸进度条、红点、resize grip 斜纹改由 GDI+ 抗锯齿绘制，圆头与斜线不再有硬锯齿；分隔线保持 1px 非 AA 锐利线
- 资源管理：后备表面按窗口尺寸缓存复用（resize 时重建），Dispose 释放 GDI+ Font/Bitmap/Graphics 与 DIB/DC；色板统一改为 RGB 定义（GDI+ Color 原生 RGB，不再需要 BGR 换算）
- 已截图验证浅/深两种主题：阴影、AA 边缘、三态药丸条、grip 均正常；resize/穿透/置顶等窗口行为不变

## [3.23.1] - 2026-10-02

### 改进：桌面悬浮窗 UI 全面完善（用户反馈三点）
- **窗口支持调整大小**：非鼠标穿透状态下，窗口右边缘 / 下边缘 / 右下角（17px 角热区）可拖拽改变尺寸（WM_NCHITTEST 返回 HTRIGHT/HTBOTTOM/HTBOTTOMRIGHT）；WM_GETMINMAXINFO 限制最小尺寸（196×118 逻辑像素）；右下角绘制三条 45° 斜纹 resize grip（仅非穿透态显示，穿透态隐藏）；宽度与高度随位置一并记忆（新增 `DesktopWidgetWidth`/`DesktopWidgetHeight` 设置，默认 -1 兼容旧 JSON），重启精确恢复
- **内容随窗口宽度等比缩放**：缩放系数 = 窗口逻辑宽 / 272（clamp 0.78~2.4），字体（13pt 标题 / 12pt 行）、行距、内边距、进度条全部联动放大，WM_SIZE 实时重建字体，解决小窗口文字模糊；未手动改过高度时窗口高度始终贴合内容
- **进度条样式重做**：细条改为圆角药丸形（RoundRect 去边框 + 灰色胶囊槽）；阈值颜色分级——安全绿 → 预警琥珀（负载≥70% / 温度≥70°C）→ **危险鲜红（负载≥90% / 温度≥85°C），数值文字同色**；标题下增加分隔线
- **网速拆分为「↓下载」「↑上传」两行**，各自独立药丸进度条；条长按自适应峰值计算（峰值每秒回落 3%，最低以 100KB/s 为基准），速率文本带 `/s` 后缀（如 `2.8M/s`）
- 温度读数规整：无权限/无传感器返回的 0°C 与超量程值显示为 `--`，不再显示误导性的 `0°`
- 修复：SetWindowPos 仅带 SWP_NOMOVE 时 cx/cy 仍会生效，高度校正漏传宽度会把窗口宽度置 0（三处改为显式传入当前宽度）
- 修复：GDI COLORREF 为 0x00BBGGRR 字节序，全部条色/文本警告色按 BGR 重新换算（此前琥珀/鲜红被错误解释为蓝色）

## [3.23.0] - 2026-10-02

### 新增：桌面硬件信息悬浮插件（P0）
- 桌面独立悬浮窗常驻显示 **CPU（含温度）/ 内存 / GPU（含温度）/ 显存 / 网速 / 磁盘活动 / 今日活跃**，每秒刷新，深浅主题自适应
- 纯 Win32 + GDI 双缓冲 + LWA_COLORKEY 色键透明实现（机制照抄项目内 TaskbarWidget），无 WinUI/WinForms 窗口依赖；**行为规格照抄 [LiteMonitor](https://github.com/Diorser/LiteMonitor)（MIT）**：无任务栏按钮、不抢焦点、置顶（3s 强制重插 + 10s 轮询维护）、WS_EX_TRANSPARENT 鼠标穿透、手动 offset 拖拽、MouseUp 时 ClampToScreen + 保存、显示器 DeviceName + 坐标位置记忆、WM_DISPLAYCHANGE 500ms 防抖恢复
- 悬浮窗可拖动，位置与所在屏幕自动记忆，重启精确恢复；贴边不弹开，仅完全跑出屏幕才拉回
- 双击悬浮窗还原主窗口并跳转硬件监控页；主窗口最小化到托盘时悬浮窗仍常驻显示
- 设置页新增「桌面悬浮插件」卡片：总开关 + 7 个显示项 + 置顶 / 鼠标穿透 / 限制拖出屏幕；设置保存后 1s 内即时生效，无需重启
- 新增设置项 `DesktopWidget*` 共 13 项（默认关闭，其余默认值照抄 LiteMonitor：置顶开、穿透关、限屏开）；旧 settings.json 缺字段自动取默认值，向后兼容

## [3.22.10]

### 稳定性
- 全量测试 154/154 通过；修复 Monitor 测试占用 8765 端口（WebSocketServer.StartAsync 改 virtual 并 stub）
- 解析不再把 reasoning_content 当正文

## [3.22.9]

### 精简
- 删除多余 Provider 标签与填充按钮，设置页更短；配置输入即保存

## [3.22.8] - 2026-09-30

### 修复：设置卡片回填被污染（根因）
- **根因**：`LoadSettingsToUI` 内部提前把 `_isLoading` 置回 `false`，随后 `AiProviderBox.SelectedItem` 赋值触发 `SelectionChanged`，将 Base URL 与模型覆盖为智能预设
- Provider 下拉**只保存标签**，不再自动改写 URL/模型；「改默认」按钮按 Provider 填充默认 URL 与模型；「恢复」按钮（显式覆盖）
- `SettingsService.Load()` 失败时**不再清空**为默认值；保存改为临时文件原子替换
- 中文输出：偏英文自动强制重试；不再把 `reasoning_content` 当正文

## [3.22.7] - 2026-09-30

### 修复：配置重复被污染回填为默认 + 测试结果不可复制
- **根因**：加载设置时 `AiProviderBox.SelectedItem = ''` 触发 `SelectionChanged`，把 Base URL 与模型覆盖成预设（智谱/glm-4-flash）
- 加载期间全程 `_isLoading` 保护；预设填充仅在用户手动切换 Provider 时
- `SaveSetting` 改为**同步 Save()**，不再异步 fire-and-forget（退出态丢配置）
- 测试结果改为可选中 TextBox + 「复制测试结果」按钮

## [3.22.6] - 2026-09-30

### 优化：API Key 明文显示（个人软件不做遮掩）
- API Key 改为明文 TextBox，测试结果直接打板完整 Key，便于和服务商控制台核对
- 请求日志输出端点/模型/Key 长度（不含 Key 明文与日志）

## [3.22.5] - 2026-09-30

### 修复：设置保存不落盘 / 401 — 端点配置收敛
- **「云端 + 本地」双轮**配置：云端 = Base URL / API Key / 模型 **最终**生效；本地 = LM Studio 最终生效
- **输入即保存**（TextChanged/PasswordChanged），不再依赖失焦按钮才落盘
- **「保存并测试连接」**：一键落盘并探测，显示 URL/模型/Key 是否已填 + 模型回复或错误
- Provider 预设改为「填充」，**不再自动覆盖**用户已填地址

## [3.22.4] - 2026-09-30

### 修复：端点配置混搭 401 + 解读英文空话
- **云端整组生效**：独立云端 URL 时必须单独填写模型，禁止与主配置 URL/模型混用；缺模型视为未配置并回退（不再对错误 Key 报 401）
- 显式指定本地/云端模型时不混淆
- **输出强制简体中文**：system + user 前缀双重锁定，confidence/effort 仍用英文枚举
- 解析失败时区分「英文空话输出」/「无结果结构」两类，附原文可复现

## [3.22.3] - 2026-09-30

### 优化：端点模式一目了然
- **「端点模式」置顶高亮卡片**：Accent 描边 + 标题提示，三选一必选
- ComboBox 改为三大单一选项：云端 / 本地 / 双端点（推荐），文案说明差异
- 底部实时显示「当前会：重复读取」

## [3.22.2] - 2026-09-30

### 修复：本地 LM Studio 跑完却「AI 返回为空」
- **本地解读改非流式**：避免 LM Studio 流式解析差异导致空正文
- 解析兼容 `message.content` / `reasoning_content` / `delta.content` / `text`
- 正文为空时抛错并附原始片段，不再静默显示「返回为空」

## [3.22.1] - 2026-09-30

### 修复：本地 AI 返回 400
- 本地端点（LM Studio/Ollama）**不再发送** `response_format` / `max_tokens`（常见 400 主因）
- 云端 400/404/422 时自动降级重试一次（剥掉可疑字段）
- 错误信息带回响应摘要，便于定位：模型名不对 / 路径不对 等问题

## [3.22.0] - 2026-09-30

### 新增：本地 + 云端双 AI 端点
- **端点模式**：云端 / 本地 LM Studio / 双端点
  - **双端点**：深度解读走云端，画像追问走本地（缺失则自动回退另一侧）
- **本地模式**：Base URL（默认 `http://127.0.0.1:1234/v1`）、模型，Key 可空
- **云端模式**：Base URL / API Key / 模型（可独立于主配置，空则回退主配置）
- 设置页分「AI 解读」与「本地/云端」分组配置，两套配置可同时保留
- 解析：`AiEndpointSlot` + `OpenAiCompatibleChatClient.Resolve`

### 测试
- `AiEndpointResolveTests`；分析模块 **100 通过**

## [3.21.1] - 2026-09-30

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.21.0] - 2026-09-30

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.20.0] - 2026-09-30

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.19.0] - 2026-09-30

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.18.0] - 2026-09-30

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.17.0] - 2026-09-30

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.16.1] - 2026-09-28

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.16.0] - 2026-09-25

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.15] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.14] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.13] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.12] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.11] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.10] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.9] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.8] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.7] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.6] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.5] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.4] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.3] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.2] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.1] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.15.0] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.14.5] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.14.4] - 2026-09-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.14.3] - 2026-09-19

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.14.2] - 2026-09-19

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.14.1] - 2026-09-19

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.14.0] - 2026-09-19

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.13.3] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.13.2] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.13.1] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.13.0] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.12.1] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.12.0] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.11.0] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.10.1] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.10.0] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.9.6] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.9.5] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.9.4] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.9.3] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.9.2] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.9.1] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.9.0] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.8.0] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.7.5] - 2026-09-18

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.7.4] - 2026-09-17

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.7.3] - 2026-09-17

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.7.2] - 2026-09-17

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.7.1] - 2026-09-17

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.7.0] - 2026-09-17

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.6.2] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.6.1] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.6.0] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.5.1] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.5.0] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.3.6] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.3.5] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.3.4] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.3.3] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.3.2] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.3.1] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.3.0] - 2026-02-12

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.2.2] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.2.1] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.2.0] - 2026-09-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.1.10] - 2026-09-12

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.1.3] - 2026-06-20

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.0.1] - 2026-06-17

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [3.0.0] - 2026-06-17

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.29.4] - 2026-06-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.29.3] - 2026-06-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.29.2] - 2026-06-16

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.26.0] - 2026-06-14

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.25.2] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.23.1] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.23.0] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.22.5] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.22.4] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.22.3] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.22.2] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.22.1] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.22.0] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.21.3] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.21.2] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.21.1] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.21.0] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。

## [2.20.1] - 2026-06-13

> 该段历史条目正文在早期提交中被错误编码（UTF-8 按 GBK 落盘）已损坏，无法逐条复原；此处仅保留版本号与日期，功能说明见对应提交记录。
