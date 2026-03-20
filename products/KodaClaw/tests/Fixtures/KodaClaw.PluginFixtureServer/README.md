# KodaClaw.PluginFixtureServer (MCP stdio fixture)

This is a minimal **MCP (Model Context Protocol) stdio server** intended to be launched as an *external process* by
`PluginHost` integration tests.

## What it exposes

- `echo` - echoes a `message` string argument
- `health_ping` - returns `"ok"` when healthy; can be forced to fail for degraded-path tests

## How to run (as an external process)

From repo root:

```bash
dotnet run --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/KodaClaw.PluginFixtureServer/KodaClaw.PluginFixtureServer.csproj
```

Degraded mode (make `health_ping` fail):

```bash
# Option A: env var
KODACLAW_FIXTURE_HEALTH_PING_FAIL=1 dotnet run --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/KodaClaw.PluginFixtureServer/KodaClaw.PluginFixtureServer.csproj

# Option B: CLI flag (remember the `--` separator for dotnet run)
dotnet run --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/KodaClaw.PluginFixtureServer/KodaClaw.PluginFixtureServer.csproj -- --fail-health-ping
```

## Notes

- The MCP protocol runs over **stdout**. This server does not print anything to stdout outside the protocol.
- All diagnostics go to **stderr**.

