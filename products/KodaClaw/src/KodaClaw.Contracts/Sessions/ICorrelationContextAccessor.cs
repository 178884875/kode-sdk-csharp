namespace KodaClaw.Contracts;

public interface ICorrelationContextAccessor
{
    string? CorrelationId { get; set; }
}
