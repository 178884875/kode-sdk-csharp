using System.Text.Json;

namespace KodaClaw.ChannelHub.Connectors.WeChat;

/// <summary>
/// 管理 get_updates_buf 游标的持久化，重启后恢复轮询位置，避免消息重放。
/// 每个账号的游标存放在独立文件：{StateDir}/sync_buf.json
/// </summary>
public sealed class WeChatAuthManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void SaveSyncBuf(string stateDir, string syncBuf)
    {
        EnsureStateDir(stateDir);
        var path = GetSyncBufPath(stateDir);
        File.WriteAllText(path, JsonSerializer.Serialize(new { sync_buf = syncBuf }, JsonOptions));
    }

    public string LoadSyncBuf(string stateDir)
    {
        var path = GetSyncBufPath(stateDir);
        if (!File.Exists(path)) return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("sync_buf", out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // 文件损坏时安全降级：从头拉取
        }

        return string.Empty;
    }

    public void ClearSyncBuf(string stateDir)
    {
        var path = GetSyncBufPath(stateDir);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string GetSyncBufPath(string stateDir) =>
        Path.Combine(stateDir, "sync_buf.json");

    private static void EnsureStateDir(string stateDir)
    {
        if (!Directory.Exists(stateDir))
            Directory.CreateDirectory(stateDir);
    }
}
