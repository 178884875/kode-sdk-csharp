using KodaClaw.Contracts;

namespace KodaClaw.Workspace;

public interface IMacOsKeychainCommandRunner
{
    Task<string?> ReadAsync(SecretRef secretRef, CancellationToken cancellationToken = default);

    Task WriteAsync(SecretRef secretRef, string secretValue, CancellationToken cancellationToken = default);

    Task DeleteAsync(SecretRef secretRef, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(SecretRef secretRef, CancellationToken cancellationToken = default);
}
