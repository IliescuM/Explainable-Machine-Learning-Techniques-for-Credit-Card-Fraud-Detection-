using System.Net.Http.Json;
using System.Text.Json;
using ExplainableFraud.Contracts.Training;

namespace ExplainableFraud.Web.Services;

public sealed class TrainingApi(HttpClient httpClient) : ITrainingApi
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Uri? BaseAddress => httpClient.BaseAddress;

    public async Task<IReadOnlyList<TrainingDatasetSummaryDto>> ListDatasetsAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync("api/training/datasets", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var list =
            await response.Content.ReadFromJsonAsync<List<TrainingDatasetSummaryDto>>(JsonReadOptions, cancellationToken)
                .ConfigureAwait(false);

        return list ?? [];
    }

    public async Task<StartTrainingJobResponse> StartJobAsync(StartTrainingJobRequest request,
        CancellationToken cancellationToken = default)
    {
        var response =
            await httpClient.PostAsJsonAsync("api/training/jobs", request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<StartTrainingJobResponse>(JsonReadOptions, cancellationToken)
               ?? throw new InvalidOperationException("Training API returned an empty body.");
    }

    public async Task<TrainingJobDto?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync($"api/training/jobs/{jobId:D}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<TrainingJobDto>(JsonReadOptions, cancellationToken)
               ?? throw new InvalidOperationException("Training API returned an empty body.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new FraudApiException((int)response.StatusCode, body);
    }
}
