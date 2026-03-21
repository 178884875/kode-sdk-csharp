using System.Text.Json;
using KodaClaw.Contracts;

namespace KodaClaw.Workspace;

public sealed class OnboardingStateService(IWorkspaceService workspaceService)
{
    private const string OnboardingStatePath = "config/onboarding.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private string GetAbsolutePath() =>
        Path.Combine(workspaceService.RootPath, OnboardingStatePath);

    public async Task<OnboardingState> GetStateAsync(CancellationToken ct = default)
    {
        try
        {
            var path = GetAbsolutePath();
            if (!File.Exists(path))
                return new OnboardingState { StartedAt = DateTimeOffset.UtcNow };

            var content = await File.ReadAllTextAsync(path, ct);
            if (string.IsNullOrEmpty(content))
                return new OnboardingState { StartedAt = DateTimeOffset.UtcNow };

            return JsonSerializer.Deserialize<OnboardingState>(content, JsonOptions)
                ?? new OnboardingState { StartedAt = DateTimeOffset.UtcNow };
        }
        catch
        {
            return new OnboardingState { StartedAt = DateTimeOffset.UtcNow };
        }
    }

    public async Task SaveStateAsync(OnboardingState state, CancellationToken ct = default)
    {
        var path = GetAbsolutePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(path, content, ct);
    }

    public async Task<OnboardingState> CompleteAsync(CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        var completed = state with { IsCompleted = true, CompletedAt = DateTimeOffset.UtcNow };
        await SaveStateAsync(completed, ct);
        return completed;
    }

    public async Task<OnboardingState> ResetAsync(CancellationToken ct = default)
    {
        var reset = new OnboardingState { StartedAt = DateTimeOffset.UtcNow };
        await SaveStateAsync(reset, ct);
        return reset;
    }
}
