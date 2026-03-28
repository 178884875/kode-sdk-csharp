namespace KodaClaw.Contracts;

public sealed record StartupRepairReportResponse(
    DateTimeOffset GeneratedAt,
    string WorkspaceRootPath,
    string ReportPath,
    RepairChecklist Checklist);
