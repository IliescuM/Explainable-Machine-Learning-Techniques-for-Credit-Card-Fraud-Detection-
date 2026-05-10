using ExplainableFraud.Application.Abstractions;
using ExplainableFraud.Contracts.Training;
using Microsoft.AspNetCore.Mvc;

namespace ExplainableFraud.Api.Controllers;

[ApiController]
[Route("api/training")]
public sealed class TrainingController(ITrainingJobService trainingJobs) : ControllerBase
{
    [HttpGet("datasets")]
    [ProducesResponseType(typeof(IReadOnlyList<TrainingDatasetSummaryDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<TrainingDatasetSummaryDto>> GetDatasets() =>
        Ok(trainingJobs.GetAvailableDatasets());

    [HttpPost("jobs")]
    [ProducesResponseType(typeof(StartTrainingJobResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StartTrainingJobResponse>> StartAsync([FromBody] StartTrainingJobRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (request.DatasetKind is TrainingDatasetKind.Unspecified)
        {
            ModelState.AddModelError(nameof(request.DatasetKind),
                $"{nameof(request.DatasetKind)} must be CreditcardKaggleDemo ({(int)TrainingDatasetKind.CreditcardKaggleDemo}) or CreditcardLocalCsv ({(int)TrainingDatasetKind.CreditcardLocalCsv}).");

            return ValidationProblem(ModelState);
        }

        if (request.ModelFamily is TrainingModelFamily.Unspecified)
        {
            ModelState.AddModelError(nameof(request.ModelFamily), "Select an ML.NET trainer family (logistic regression, FastTree, or FastForest).");
            return ValidationProblem(ModelState);
        }

        if (request.ModelFamily is TrainingModelFamily.NeuralNetworkExperimentalPlaceholder)
        {
            return Problem(
                detail:
                "Experimental neural/tabular stacks are intentionally disabled — ML.NET does not expose first-class differentiable networks for PCA features here; orchestrate ONNX/Torch or Python training externally.",
                title: "Trainer not available",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var outcome = await trainingJobs
            .StartJobAsync(request.DatasetKind, request.ModelFamily, cancellationToken)
            .ConfigureAwait(false);
        if (!outcome.Success)
        {
            return Problem(
                detail: outcome.ErrorMessage,
                title: "Cannot start training",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var body = new StartTrainingJobResponse { JobId = outcome.JobId };
        return CreatedAtAction(nameof(GetJob), new { jobId = outcome.JobId }, body);
    }

    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(TrainingJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<TrainingJobDto> GetJob(Guid jobId)
    {
        var job = trainingJobs.GetJob(jobId);
        if (job == null)
            return NotFound();

        return Ok(job);
    }
}
