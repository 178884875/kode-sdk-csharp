using System.Text.Json;
using System.Text.Json.Serialization;

namespace KodaClaw.Cli;

public sealed class KcConfig
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kc-cli");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [JsonPropertyName("gatewayUrl")] public string? GatewayUrl { get; init; }
    [JsonPropertyName("token")] public string? Token { get; init; }

    public static KcConfig? Load()
    {
        if (!File.Exists(ConfigFile)) return null;
        try
        {
            var json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<KcConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string token, string gatewayUrl)
    {
        Directory.CreateDirectory(ConfigDir);
        var config = new KcConfig { Token = token, GatewayUrl = gatewayUrl };
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config, JsonOptions));
    }

    /// <summary>Priority: env var > config file > default</summary>
    public static string ResolveGatewayUrl()
    {
        return Environment.GetEnvironmentVariable("KODACLAW_GATEWAY_URL")
            ?? Load()?.GatewayUrl
            ?? "http://127.0.0.1:5076";
    }

    /// <summary>Priority: env var > config file</summary>
    public static string? ResolveToken()
    {
        return Environment.GetEnvironmentVariable("KODACLAW_GATEWAY_TOKEN")
            ?? Load()?.Token;
    }
}
