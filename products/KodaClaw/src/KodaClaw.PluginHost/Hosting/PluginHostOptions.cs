namespace KodaClaw.PluginHost.Hosting;

public sealed class PluginHostOptions
{
    public int MaxRestartAttempts { get; set; } = 2;

    public int DefaultHealthcheckTimeoutSeconds { get; set; } = 5;
}
