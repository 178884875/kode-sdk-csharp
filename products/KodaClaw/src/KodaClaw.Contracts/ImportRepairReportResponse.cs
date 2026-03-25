namespace KodaClaw.Contracts;

public sealed record ImportRepairReportResponse(
    DateTimeOffset GeneratedAt,
    string WorkspaceRootPath,
    string ReportPath,
    RepairChecklist Checklist);
