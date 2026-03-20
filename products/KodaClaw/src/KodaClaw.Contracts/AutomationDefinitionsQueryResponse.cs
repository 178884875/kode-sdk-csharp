namespace KodaClaw.Contracts;

public sealed record AutomationDefinitionsQueryResponse(
    IReadOnlyList<AutomationDefinition> Items);
