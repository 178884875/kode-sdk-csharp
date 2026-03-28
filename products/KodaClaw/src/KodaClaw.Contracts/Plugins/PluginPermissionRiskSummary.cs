namespace KodaClaw.Contracts;

public sealed record PluginPermissionRiskSummary(
    IReadOnlyList<string> HighRiskReasons,
    IReadOnlyList<string> MediumRiskReasons)
{
    public bool HasHighRisk => HighRiskReasons.Count > 0;
}
