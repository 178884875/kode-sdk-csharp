using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;

namespace KodaClaw.Runtime;

public interface IAutomationSessionService
{
    Task<AutomationSessionHandle> StartAutomationSessionAsync(
        AutomationDefinition definition,
        CancellationToken cancellationToken = default);
}

public sealed record AutomationSessionHandle(
    string SessionId,
    string AutomationId,
    SessionKind SessionKind,
    string SessionDirectory,
    IAgent Agent);
