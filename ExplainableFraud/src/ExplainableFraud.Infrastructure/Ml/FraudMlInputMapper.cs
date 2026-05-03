using ExplainableFraud.Domain.Transactions;

namespace ExplainableFraud.Infrastructure.Ml;

public static class FraudMlInputMapper
{
    public static FraudMlInput FromDomain(TransactionFeatures transaction)
    {
        var v = new float[28];
        for (var i = 0; i < 28; i++)
            v[i] = transaction.PrincipalComponents[i];

        return new FraudMlInput
        {
            Time = transaction.TimeSeconds,
            Amount = transaction.Amount,
            V = v
        };
    }
}
