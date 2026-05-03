using Microsoft.ML.Data;

namespace ExplainableFraud.Infrastructure.Ml;

/// <summary>Feature vector layout: Time, V1..V28 (PCA), Amount — matches Kaggle creditcard.csv semantics.</summary>
public class FraudMlInput
{
    public float Time { get; set; }

    [VectorType(28)]
    public float[] V { get; set; } = new float[28];

    public float Amount { get; set; }
}
