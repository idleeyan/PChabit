using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Services;

/// <summary>便签 WebDAV 云同步结果。</summary>
public sealed record StickyNoteSyncResult(bool Ok, string Message, int LocalCount, int CloudCount, int ConflictCount);

/// <summary>
/// 便签云同步（3.24.0）：复用 WebDAV 账号，云文件 pchabit/sticky-notes-v1.json。
/// 全量合并（便签体积小）：按 Id full-outer-join，LWW（UpdatedAt）决胜，
/// 删除经墓碑（IsDeleted+DeletedAt）传播；双端同改正文时旧版追加到胜者正文底部留痕。
/// 窗口布局字段（Window*/IsOnDesktop）为本机私有，从不上云、不被覆盖。
/// </summary>
public sealed class StickyNoteSyncService
{
    public const string CloudFilePath = "pchabit/sticky-notes-v1.json";
    private static readonly TimeSpan TombstoneLifetime = TimeSpan.FromDays(30);

    private readonly IStickyNoteService _notes;
    private readonly IWebDAVSyncService _webDav;
    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public StickyNoteSyncService(IStickyNoteService notes, IWebDAVSyncService webDav, ISettingsService settings)
    {
        _notes = notes;
        _webDav = webDav;
        _settings = settings;
    }

    public async Task<StickyNoteSyncResult> SyncAsync(CancellationToken ct = default)
    {
        if (!await _lock.WaitAsync(0, ct))
            return new StickyNoteSyncResult(false, "便签同步进行中，请稍候", 0, 0, 0);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(150));
            ct = cts.Token;

            if (!_settings.WebDAVEnabled || string.IsNullOrWhiteSpace(_settings.WebDAVUrl))
                return new StickyNoteSyncResult(false, "未启用 WebDAV，跳过便签同步", 0, 0, 0);

            var url = _settings.WebDAVUrl;
            var user = _settings.WebDAVUsername;
            var pass = _settings.WebDAVPassword;

            var local = await _notes.GetAllForSyncAsync();
            var cloud = await DownloadCloudAsync(url, user, pass, ct);

            var (merged, conflicts) = Merge(local, cloud);

            await _notes.ReplaceAllForSyncAsync(merged);

            // 上传：剔除删除已满 30 天的墓碑（其余设备在此期间内仍能收到删除）
            var toUpload = merged
                .Where(n => !(n.IsDeleted && n.DeletedAt != null && DateTime.Now - n.DeletedAt > TombstoneLifetime))
                .ToList();
            var payload = new CloudFile
            {
                Version = 1,
                DeviceId = _settings.StickyNotesDeviceId,
                ExportedAt = DateTime.UtcNow,
                Notes = toUpload.Select(ToDto).ToList()
            };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            await _webDav.UploadFileAsync(url, user, pass, CloudFilePath, Encoding.UTF8.GetBytes(json), ct);

            _settings.StickyNotesLastSync = DateTime.Now;
            _settings.Save();

            var msg = conflicts > 0
                ? $"同步完成（本地 {local.Count} / 云端 {cloud.Count}，{conflicts} 条内容冲突已保留双版本）"
                : $"同步完成（本地 {local.Count} / 云端 {cloud.Count}）";
            Log.Information("[StickyNoteSync] {Msg}", msg);
            return new StickyNoteSyncResult(true, msg, local.Count, cloud.Count, conflicts);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[StickyNoteSync] 同步失败");
            return new StickyNoteSyncResult(false, "同步失败：" + ex.Message, 0, 0, 0);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>下载并解析云文件；404/不存在视为空集。坏 JSON 备份为 .corrupt 后按空集处理。</summary>
    private async Task<List<StickyNote>> DownloadCloudAsync(
        string url, string user, string pass, CancellationToken ct)
    {
        var bytes = await _webDav.DownloadFileAsync(url, user, pass, CloudFilePath, ct);
        if (bytes == null || bytes.Length == 0) return new List<StickyNote>();

        try
        {
            var file = JsonSerializer.Deserialize<CloudFile>(bytes, JsonOptions);
            if (file?.Notes == null) return new List<StickyNote>();
            return file.Notes.Select(ToEntity).Where(n => n != null).Cast<StickyNote>().ToList();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "[StickyNoteSync] 云端文件损坏，备份后按空集重建");
            try
            {
                var backup = new CloudFile { Version = 1, DeviceId = "corrupt", ExportedAt = DateTime.UtcNow, Notes = new() };
                var corruptName = $"pchabit/sticky-notes-corrupt-{DateTime.Now:yyyyMMddHHmmss}.json";
                await _webDav.UploadFileAsync(url, user, pass, corruptName, bytes, ct);
            }
            catch { /* 备份失败不阻塞 */ }
            return new List<StickyNote>();
        }
    }

    /// <summary>
    /// 按 Id 全外连接合并。
    /// 规则：单边记录直接采用；双边记录整条 LWW（UpdatedAt 较新者胜）；
    /// 双方都未删除且正文内容不同（双端各自改过）时，旧版正文前 2000 字追加到胜者底部供人工合并。
    /// </summary>
    internal static (List<StickyNote> Merged, int Conflicts) Merge(
        IReadOnlyCollection<StickyNote> localList, IReadOnlyCollection<StickyNote> cloudList)
    {
        var local = localList.ToDictionary(n => n.Id);
        var cloud = cloudList.ToDictionary(n => n.Id);
        var result = new List<StickyNote>(local.Count + cloud.Count);
        int conflicts = 0;

        foreach (var id in local.Keys.Union(cloud.Keys))
        {
            bool hasL = local.TryGetValue(id, out var l);
            bool hasC = cloud.TryGetValue(id, out var c);

            if (hasL && !hasC) { result.Add(l!); continue; }
            if (hasC && !hasL) { result.Add(c!); continue; }

            // 双端都有：LWW
            var winner = l!.UpdatedAt >= c!.UpdatedAt ? l : c;
            var loser = ReferenceEquals(winner, l) ? c : l;

            // 墓碑优先：较新一方若已删除，删除生效（UpdatedAt 已包含删除时间戳）
            // 双端都未删且正文各自不同 → 冲突留痕
            bool bothAlive = !l.IsDeleted && !c.IsDeleted;
            bool contentDiverged = bothAlive && l.Content != c.Content
                && l.Content.Trim().Length > 0 && c.Content.Trim().Length > 0;
            if (contentDiverged)
            {
                conflicts++;
                var tail = loser.Content.Length > 2000 ? loser.Content[..2000] + "…" : loser.Content;
                winner.Content = winner.Content.TrimEnd()
                    + $"\n\n—— 来自设备 {ShortDevice(loser.DeviceId)} 的旧版本（{loser.UpdatedAt:MM-dd HH:mm}）——\n"
                    + tail.TrimEnd();
            }

            // 复制胜出实体，避免污染本地跟踪实体
            result.Add(new StickyNote
            {
                Id = winner.Id,
                Content = winner.Content,
                Color = winner.Color,
                SortOrder = winner.SortOrder,
                IsPinned = winner.IsPinned,
                IsArchived = winner.IsArchived,
                IsDeleted = winner.IsDeleted,
                CreatedAt = l.CreatedAt < c.CreatedAt ? l.CreatedAt : c.CreatedAt,
                UpdatedAt = winner.UpdatedAt,
                DeletedAt = winner.DeletedAt,
                DeviceId = winner.DeviceId,
                Version = Math.Max(l.Version, c.Version),
                // 窗口字段：本机值由 ReplaceAllForSyncAsync 保留，这里给默认
                IsOnDesktop = false,
                WindowLeft = -1,
                WindowTop = -1,
                WindowWidth = 0,
                WindowHeight = 0
            });
        }

        return (result, conflicts);
    }

    private static string ShortDevice(string deviceId) =>
        string.IsNullOrWhiteSpace(deviceId) ? "未知" : deviceId.Length > 6 ? deviceId[..6] : deviceId;

    // ---------- DTO ----------

    private sealed class CloudFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
        [JsonPropertyName("exportedAt")] public DateTime ExportedAt { get; set; }
        [JsonPropertyName("notes")] public List<NoteDto> Notes { get; set; } = new();
    }

    private sealed class NoteDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
        [JsonPropertyName("color")] public string Color { get; set; } = "yellow";
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("isPinned")] public bool IsPinned { get; set; }
        [JsonPropertyName("isArchived")] public bool IsArchived { get; set; }
        [JsonPropertyName("isDeleted")] public bool IsDeleted { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
        [JsonPropertyName("deletedAt")] public DateTime? DeletedAt { get; set; }
        [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
    }

    private static NoteDto ToDto(StickyNote n) => new()
    {
        Id = n.Id.ToString(),
        Content = n.Content,
        Color = n.Color,
        SortOrder = n.SortOrder,
        IsPinned = n.IsPinned,
        IsArchived = n.IsArchived,
        IsDeleted = n.IsDeleted,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt,
        DeletedAt = n.DeletedAt,
        DeviceId = n.DeviceId,
        Version = n.Version
    };

    private static StickyNote? ToEntity(NoteDto d)
    {
        if (!Guid.TryParse(d.Id, out var id)) return null;
        return new StickyNote
        {
            Id = id,
            Content = d.Content ?? "",
            Color = string.IsNullOrWhiteSpace(d.Color) ? "yellow" : d.Color,
            SortOrder = d.SortOrder,
            IsPinned = d.IsPinned,
            IsArchived = d.IsArchived,
            IsDeleted = d.IsDeleted,
            CreatedAt = d.CreatedAt == default ? DateTime.Now : d.CreatedAt,
            UpdatedAt = d.UpdatedAt == default ? DateTime.Now : d.UpdatedAt,
            DeletedAt = d.DeletedAt,
            DeviceId = d.DeviceId ?? "",
            Version = d.Version <= 0 ? 1 : d.Version
        };
    }
}
