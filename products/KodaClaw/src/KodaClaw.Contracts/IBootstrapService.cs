namespace KodaClaw.Contracts;

public interface IBootstrapService
{
    Task<BootstrapCompletionResult> CompleteAsync(
        BootstrapCompletionRequest request,
        CancellationToken cancellationToken = default);
}
