namespace KodaClaw.Contracts;

public interface IBootstrapDraftService
{
    Task<BootstrapDraftResult> GenerateDraftAsync(
        BootstrapDraftRequest request,
        CancellationToken cancellationToken = default);
}
