using Microsoft.ML.Data;

namespace ExplainableFraud.Infrastructure.Ml;

public sealed class FraudTrainingObservation : FraudMlInput
{
    [ColumnName("Label")] public bool Label { get; set; }
}
