using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace KodaClaw.Gateway;

internal static class GatewayConfigurationBootstrap
{
    public static void LoadCurrentDirectoryDotEnvFiles(string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        var loadedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LoadDotEnvFile(Path.Combine(currentDirectory, ".env"), loadedKeys);
        LoadDotEnvFile(Path.Combine(currentDirectory, ".env.local"), loadedKeys);
    }

    public static void ApplyCurrentDirectoryJsonFiles(
        ConfigurationManager configuration,
        string currentDirectory,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        var fileProvider = new PhysicalFileProvider(currentDirectory);
        var insertIndex = FindSourceInsertIndex(configuration.Sources);

        InsertJsonSource(configuration.Sources, fileProvider, "appsettings.json", insertIndex++);
        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            InsertJsonSource(
                configuration.Sources,
                fileProvider,
                $"appsettings.{environmentName}.json",
                insertIndex);
        }
    }

    private static void LoadDotEnvFile(string path, ISet<string> loadedKeys)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var pair in ParseDotEnvFile(path))
        {
            var existing = Environment.GetEnvironmentVariable(pair.Key);
            if (existing is not null && !loadedKeys.Contains(pair.Key))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            loadedKeys.Add(pair.Key);
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseDotEnvFile(string path)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim();
            yield return new KeyValuePair<string, string>(key, Unquote(value));
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            var first = value[0];
            var last = value[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                return value[1..^1];
            }
        }

        return value;
    }

    private static int FindSourceInsertIndex(IList<IConfigurationSource> sources)
    {
        for (var index = 0; index < sources.Count; index++)
        {
            if (sources[index] is EnvironmentVariablesConfigurationSource or CommandLineConfigurationSource)
            {
                return index;
            }
        }

        return sources.Count;
    }

    private static void InsertJsonSource(
        IList<IConfigurationSource> sources,
        IFileProvider fileProvider,
        string path,
        int index)
    {
        var source = new JsonConfigurationSource
        {
            FileProvider = fileProvider,
            Path = path,
            Optional = true,
            ReloadOnChange = true,
        };
        source.ResolveFileProvider();
        sources.Insert(index, source);
    }
}
