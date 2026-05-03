using ExplainableFraud.Application.Abstractions;
using ExplainableFraud.Application.Mapping;
using ExplainableFraud.Contracts.Fraud;
using Microsoft.AspNetCore.Mvc;

namespace ExplainableFraud.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class FraudController(IFraudScoringService scoringService) : ControllerBase
{
    [HttpPost("score")]
    [ProducesResponseType(typeof(FraudScoreResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FraudScoreResponse>> ScoreAsync(
        [FromBody] FraudScoreRequest request,
        CancellationToken cancellationToken)
    {
        var features = TransactionMapper.ToDomain(request);
        var result = await scoringService.ScoreAsync(features, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }
}
