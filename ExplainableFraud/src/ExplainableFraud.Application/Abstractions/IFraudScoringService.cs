using ExplainableFraud.Contracts.Fraud;
using ExplainableFraud.Domain.Transactions;

namespace ExplainableFraud.Application.Abstractions;

public interface IFraudScoringService
{
    Task<FraudScoreResponse> ScoreAsync(TransactionFeatures transaction, CancellationToken cancellationToken = default);
}
