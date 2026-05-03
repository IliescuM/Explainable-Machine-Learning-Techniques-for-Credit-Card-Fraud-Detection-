using Microsoft.ML.Data;

namespace ExplainableFraud.Infrastructure.Ml;

public sealed class FraudMlOutput
{
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    [ColumnName("Score")]
    public float Score { get; set; }

    [ColumnName("Probability")]
    public float Probability { get; set; }
}
