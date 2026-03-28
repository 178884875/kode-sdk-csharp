using KodaClaw.Contracts;

namespace KodaClaw.Workspace;

public interface IHeartbeatAutomationCompiler
{
    IReadOnlyList<AutomationDefinition> Compile(string markdown);
}
