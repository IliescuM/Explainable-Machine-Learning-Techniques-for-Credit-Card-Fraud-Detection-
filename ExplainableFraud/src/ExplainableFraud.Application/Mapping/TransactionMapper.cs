using ExplainableFraud.Contracts.Fraud;
using ExplainableFraud.Domain.Transactions;

namespace ExplainableFraud.Application.Mapping;

public static class TransactionMapper
{
    public const int ExpectedPrincipalDimensions = 28;

    public static TransactionFeatures ToDomain(FraudScoreRequest request)
    {
        var pcs = NormalizePrincipalComponents(request.PrincipalComponents);
        return new TransactionFeatures(request.Amount, request.Time, pcs);
    }

    private static IReadOnlyList<float> NormalizePrincipalComponents(float[]? source)
    {
        var buf = new float[ExpectedPrincipalDimensions];
        if (source is null)
            return buf;

        var n = Math.Min(source.Length, ExpectedPrincipalDimensions);
        for (var i = 0; i < n; i++)
            buf[i] = source[i];

        return buf;
    }
}
