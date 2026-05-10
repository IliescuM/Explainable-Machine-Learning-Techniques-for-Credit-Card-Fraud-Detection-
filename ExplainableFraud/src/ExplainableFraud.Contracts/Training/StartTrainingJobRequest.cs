using System.ComponentModel.DataAnnotations;

namespace ExplainableFraud.Contracts.Training;

public sealed class StartTrainingJobRequest
{
    [Required]
    [EnumDataType(typeof(TrainingDatasetKind))]
    public TrainingDatasetKind DatasetKind { get; set; }

    [EnumDataType(typeof(TrainingModelFamily))]
    public TrainingModelFamily ModelFamily { get; set; } = TrainingModelFamily.LogisticRegressionBaseline;
}
