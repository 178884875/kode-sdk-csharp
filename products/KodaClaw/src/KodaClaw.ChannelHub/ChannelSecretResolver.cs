using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub;

internal sealed class ChannelSecretResolver
{
    private readonly ISecretStore _secretStore;

    public ChannelSecretResolver(ISecretStore? secretStore = null)
    {
        _secretStore = secretStore ?? NullSecretStore.Instance;
    }

    public string? Resolve(
        string? explicitSecret,
        string? credentialReferenceFromConfig,
        string? credentialReferenceFromAccount)
    {
        var normalizedExplicitSecret = NormalizeNullable(explicitSecret);
        if (!string.IsNullOrWhiteSpace(normalizedExplicitSecret))
        {
            return normalizedExplicitSecret;
        }

        var reference = NormalizeNullable(credentialReferenceFromConfig)
            ?? NormalizeNullable(credentialReferenceFromAccount);
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (SecretRef.TryParse(reference, out var secretRef))
        {
            var resolved = _secretStore.GetAsync(secretRef).GetAwaiter().GetResult();
            return NormalizeNullable(resolved);
        }

        if (reference.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var environmentKey = reference["env:".Length..].Trim();
            if (environmentKey.Length == 0)
            {
                return null;
            }

            return NormalizeNullable(Environment.GetEnvironmentVariable(environmentKey));
        }

        if (reference.StartsWith("inline:", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeNullable(reference["inline:".Length..]);
        }

        if (reference.StartsWith("value:", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeNullable(reference["value:".Length..]);
        }

        return NormalizeNullable(reference);
    }

    private static string? NormalizeNullable(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private sealed class NullSecretStore : ISecretStore
    {
        public static NullSecretStore Instance { get; } = new();

        public Task<string?> GetAsync(SecretRef secretRef, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task UpsertAsync(SecretRef secretRef, string secretValue, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("A writable secret store was not configured.");
        }

        public Task DeleteAsync(SecretRef secretRef, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("A writable secret store was not configured.");
        }

        public Task<SecretDescriptor> DescribeAsync(SecretRef secretRef, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SecretDescriptor(
                secretRef,
                Exists: false,
                IsReadOnly: true,
                StorageDisplayName: "Unavailable Secret Store"));
        }
    }
}
