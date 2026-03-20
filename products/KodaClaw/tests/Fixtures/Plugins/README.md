# Bundled Plugin Fixtures

These fixture manifests are used by `KC-0408` to verify bundled plugin discovery and
Iteration 4 acceptance flows.

Available bundled fixtures:

- `plugin.bundled.fixture` - healthy stdio tool plugin backed by `KodaClaw.PluginFixtureServer`
- `plugin.bundled.degraded` - degraded variant that forces `health_ping` to fail

Both manifests reference the built fixture server DLL via a relative path:

- `../../KodaClaw.PluginFixtureServer/bin/Debug/net10.0/KodaClaw.PluginFixtureServer.dll`

Before running the manual dogfood drill, build the fixture server once:

```bash
dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/KodaClaw.PluginFixtureServer/KodaClaw.PluginFixtureServer.csproj
```
