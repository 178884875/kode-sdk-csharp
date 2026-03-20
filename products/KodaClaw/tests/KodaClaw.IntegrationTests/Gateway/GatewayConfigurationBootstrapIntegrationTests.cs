using FluentAssertions;
using KodaClaw.Gateway;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KodaClaw.IntegrationTests.Gateway;

public sealed class GatewayConfigurationBootstrapIntegrationTests
{
    [Fact]
    public void LoadCurrentDirectoryDotEnvFiles_should_seed_missing_values_and_keep_real_environment_overrides()
    {
        using var directory = new TempDirectory();
        var dotenvOnlyKey = $"KODACLAW_DOTENV_ONLY_{Guid.NewGuid():N}";
        var dotenvLocalOverrideKey = $"KODACLAW_DOTENV_LOCAL_{Guid.NewGuid():N}";
        var existingEnvironmentKey = $"KODACLAW_DOTENV_ENV_{Guid.NewGuid():N}";

        File.WriteAllText(
            Path.Combine(directory.Path, ".env"),
            $"{dotenvOnlyKey}=from-dotenv{Environment.NewLine}{dotenvLocalOverrideKey}=from-dotenv{Environment.NewLine}{existingEnvironmentKey}=from-dotenv");
        File.WriteAllText(
            Path.Combine(directory.Path, ".env.local"),
            $"{dotenvLocalOverrideKey}=from-dotenv-local{Environment.NewLine}{existingEnvironmentKey}=from-dotenv-local");

        Environment.SetEnvironmentVariable(existingEnvironmentKey, "from-environment");

        try
        {
            GatewayConfigurationBootstrap.LoadCurrentDirectoryDotEnvFiles(directory.Path);

            Environment.GetEnvironmentVariable(dotenvOnlyKey).Should().Be("from-dotenv");
            Environment.GetEnvironmentVariable(dotenvLocalOverrideKey).Should().Be("from-dotenv-local");
            Environment.GetEnvironmentVariable(existingEnvironmentKey).Should().Be("from-environment");
        }
        finally
        {
            Environment.SetEnvironmentVariable(dotenvOnlyKey, null);
            Environment.SetEnvironmentVariable(dotenvLocalOverrideKey, null);
            Environment.SetEnvironmentVariable(existingEnvironmentKey, null);
        }
    }

    [Fact]
    public void ApplyCurrentDirectoryJsonFiles_should_keep_environment_and_command_line_precedence()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "appsettings.json"),
            """
            {
              "KODACLAW_DEFAULT_MODEL": "from-json",
              "Runtime": {
                "OpenAIBaseUrl": "https://json-base"
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(directory.Path, "appsettings.Development.json"),
            """
            {
              "Runtime": {
                "OpenAIBaseUrl": "https://json-development"
              }
            }
            """);

        Environment.SetEnvironmentVariable("KODACLAW_DEFAULT_MODEL", "from-environment");

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddEnvironmentVariables();
            configuration.AddCommandLine(["--KODACLAW_DEFAULT_MODEL=from-command-line"]);

            GatewayConfigurationBootstrap.ApplyCurrentDirectoryJsonFiles(
                configuration,
                directory.Path,
                environmentName: "Development");

            configuration["KODACLAW_DEFAULT_MODEL"].Should().Be("from-command-line");
            configuration["Runtime:OpenAIBaseUrl"].Should().Be("https://json-development");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KODACLAW_DEFAULT_MODEL", null);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "kodaclaw-config-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
