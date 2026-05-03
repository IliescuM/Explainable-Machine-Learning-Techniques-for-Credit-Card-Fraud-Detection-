namespace ExplainableFraud.Contracts.Fraud;

public sealed class FraudScoreRequest
{
    public float Amount { get; init; }
    public float Time { get; init; }
    public float[]? PrincipalComponents { get; init; }
}
