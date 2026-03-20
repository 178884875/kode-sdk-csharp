namespace KodaClaw.Contracts;

public sealed record AutomationDefinitionQuery(
    bool? Enabled = null,
    AutomationDefinitionSource? Source = null,
    int Limit = 50);
