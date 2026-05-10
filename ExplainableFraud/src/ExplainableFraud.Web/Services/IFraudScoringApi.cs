using ExplainableFraud.Contracts.Fraud;

namespace ExplainableFraud.Web.Services;

public interface IFraudScoringApi
{
    Uri? BaseAddress { get; }

    Task<FraudScoreResponse> ScoreAsync(FraudScoreRequest request, CancellationToken cancellationToken = default);
}
