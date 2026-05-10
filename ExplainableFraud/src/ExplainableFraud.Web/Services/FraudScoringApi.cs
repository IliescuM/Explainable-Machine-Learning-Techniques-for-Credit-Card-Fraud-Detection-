using System.Net.Http.Json;
using ExplainableFraud.Contracts.Fraud;

namespace ExplainableFraud.Web.Services;

public sealed class FraudScoringApi(HttpClient httpClient) : IFraudScoringApi
{
    public Uri? BaseAddress => httpClient.BaseAddress;

    public async Task<FraudScoreResponse> ScoreAsync(FraudScoreRequest request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/fraud/score", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new FraudApiException((int)response.StatusCode, body);
        }

        return await response.Content.ReadFromJsonAsync<FraudScoreResponse>(cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Fraud scoring API returned an empty response.");
    }
}
