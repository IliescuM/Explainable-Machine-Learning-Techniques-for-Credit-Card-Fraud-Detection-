namespace ExplainableFraud.Domain.Transactions;

/// <summary>
/// Canonical feature snapshot for scoring (classic credit-card fraud layout: Amount, Time + PCA dims).
/// </summary>
public sealed record TransactionFeatures(
    float Amount,
    float TimeSeconds,
    IReadOnlyList<float> PrincipalComponents);
