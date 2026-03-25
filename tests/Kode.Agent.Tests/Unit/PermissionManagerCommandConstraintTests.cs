using Kode.Agent.Sdk.Core.Agent;
using Xunit;

namespace Kode.Agent.Tests.Unit;

public class PermissionManagerCommandConstraintTests
{
    // ── ContainsShellMetachar ─────────────────────────────────────────────────

    [Theory]
    [InlineData("kc automation list; rm -rf ~", true)]
    [InlineData("git status && rm -rf .", true)]
    [InlineData("git log || echo oops", true)]
    [InlineData("echo `whoami`", true)]
    [InlineData("echo $(cat /etc/passwd)", true)]
    [InlineData("cat file > output.txt", true)]
    [InlineData("cat a >> b", true)]
    [InlineData("cat < input.txt", true)]
    [InlineData("ps aux | grep kc", true)]
    public void ContainsShellMetachar_detects_dangerous_patterns(string command, bool expected)
    {
        Assert.Equal(expected, PermissionManager.ContainsShellMetachar(command));
    }

    [Theory]
    [InlineData("kc automation list --json")]
    [InlineData("git status")]
    [InlineData("kc workspace status")]
    [InlineData("kc automation run heartbeat --json")]
    public void ContainsShellMetachar_clean_commands_return_false(string command)
    {
        Assert.False(PermissionManager.ContainsShellMetachar(command));
    }

    // ── GrantTools + IsCommandWhitelisted ─────────────────────────────────────

    [Fact]
    public void GrantTools_constraint_spec_adds_command_prefix_whitelist()
    {
        var mgr = BuildManager();
        mgr.GrantTools(["bash_run[kc]"]);

        Assert.True(mgr.IsCommandWhitelisted("bash_run", "kc automation list --json"));
    }

    [Fact]
    public void GrantTools_multiple_constraints_are_merged()
    {
        var mgr = BuildManager();
        mgr.GrantTools(["bash_run[kc]", "bash_run[git]"]);

        Assert.True(mgr.IsCommandWhitelisted("bash_run", "kc workspace status"));
        Assert.True(mgr.IsCommandWhitelisted("bash_run", "git status"));
    }

    [Fact]
    public void IsCommandWhitelisted_non_whitelisted_prefix_returns_false()
    {
        var mgr = BuildManager();
        mgr.GrantTools(["bash_run[kc]"]);

        Assert.False(mgr.IsCommandWhitelisted("bash_run", "rm -rf ~"));
    }

    [Fact]
    public void IsCommandWhitelisted_shell_metachar_always_returns_false()
    {
        var mgr = BuildManager();
        mgr.GrantTools(["bash_run[kc]"]);

        // Even though command starts with 'kc', semicolon makes it dangerous
        Assert.False(mgr.IsCommandWhitelisted("bash_run", "kc list; rm -rf ~"));
    }

    [Fact]
    public void IsCommandWhitelisted_no_constraints_granted_returns_false()
    {
        var mgr = BuildManager();
        // No GrantTools called

        Assert.False(mgr.IsCommandWhitelisted("bash_run", "kc automation list"));
    }

    [Fact]
    public void GrantTools_plain_tool_name_does_not_add_constraints()
    {
        var mgr = BuildManager();
        mgr.GrantTools(["bash_run"]);

        // Plain bash_run grant should not add constraint → IsCommandWhitelisted still false
        Assert.False(mgr.IsCommandWhitelisted("bash_run", "kc automation list"));
    }

    [Fact]
    public void IsCommandWhitelisted_uses_basename_for_path_commands()
    {
        var mgr = BuildManager();
        mgr.GrantTools(["bash_run[kc]"]);

        // /usr/local/bin/kc should match "kc" prefix (basename extraction)
        Assert.True(mgr.IsCommandWhitelisted("bash_run", "/usr/local/bin/kc automation list"));
    }

    // ── GrantTools: plain names still work ────────────────────────────────────

    [Fact]
    public void GrantTools_plain_name_adds_to_allowlist_without_constraint()
    {
        // Smoke: GrantTools with plain name should not throw
        var mgr = BuildManager();
        mgr.GrantTools(["fs_read", "fs_write"]);
        // No assertion needed — just verify no exception
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PermissionManager BuildManager()
    {
        var bus = new Kode.Agent.Sdk.Core.Events.EventBus();
        return new PermissionManager(bus, null, []);
    }
}
