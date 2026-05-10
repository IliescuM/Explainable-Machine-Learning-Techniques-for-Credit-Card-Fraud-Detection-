namespace ExplainableFraud.Infrastructure.Ml;

/// <summary>Deterministic creditcard-shaped rows for reproducible demos (not the real CSV).</summary>
public static class SyntheticCreditcardStyleDataset
{
    public static IReadOnlyList<FraudTrainingObservation> Generate(int rowCount, int seed, float fraudFraction = 0.05f)
    {
        if (rowCount < 128)
            throw new ArgumentOutOfRangeException(nameof(rowCount), "Need enough rows for a stable train/test split.");

        fraudFraction = Math.Clamp(fraudFraction, 0.01f, 0.4f);

        var rnd = new Random(seed);
        var list = new List<FraudTrainingObservation>(rowCount);
        var fraudTarget = Math.Max(8, (int)Math.Round(rowCount * fraudFraction));
        fraudTarget = Math.Min(fraudTarget, rowCount - 8);

        var fraudSlots = Enumerable.Range(0, rowCount).OrderBy(_ => rnd.NextDouble()).Take(fraudTarget).ToHashSet();

        for (var i = 0; i < rowCount; i++)
        {
            var isFraud = fraudSlots.Contains(i);
            list.Add(RandomRow(isFraud, rnd));
        }

        return list;
    }

    /// <remarks>Produces slightly separated clusters so models have something to fit.</remarks>
    private static FraudTrainingObservation RandomRow(bool fraud, Random rnd)
    {
        float[] v =
        [
            Gaussian(rnd) + (fraud ? 1.35f : 0f),
            Gaussian(rnd) + (fraud ? 0.95f : 0f),
            Gaussian(rnd) - (fraud ? 1.05f : 0f),
            Gaussian(rnd) + (fraud ? 0.62f : 0f),
            Gaussian(rnd) - (fraud ? 1.62f : 0f),
            Gaussian(rnd) + (fraud ? 2.42f : 0f),
            Gaussian(rnd),
            Gaussian(rnd) + (fraud ? -0.9f : 0f),
            Gaussian(rnd),
            Gaussian(rnd) + (fraud ? -1.82f : 0f),
            Gaussian(rnd) + (fraud ? 1.92f : 0f),
            Gaussian(rnd) - (fraud ? 3.82f : 0f),
            Gaussian(rnd) + (fraud ? 1.92f : 0f),
            Gaussian(rnd) + (fraud ? 1.92f : 0f),
            Gaussian(rnd) + (fraud ? 1.92f : 0f),
            Gaussian(rnd) + (fraud ? 1.92f : 0f),
            Gaussian(rnd) + (fraud ? 3.92f : 0f),
            Gaussian(rnd) + (fraud ? 7.92f : 0f),
            Gaussian(rnd),
            Gaussian(rnd) + (fraud ? -2.92f : 0f),
            Gaussian(rnd) + (fraud ? -2.92f : 0f),
            Gaussian(rnd) + (fraud ? 2f : 0f),
            Gaussian(rnd) + (fraud ? 2f : 0f),
            Gaussian(rnd) + (fraud ? 2f : 0f),
            Gaussian(rnd) + (fraud ? 2f : 0f),
            Gaussian(rnd) + (fraud ? 9f : 0f),
            Gaussian(rnd),
            Gaussian(rnd),
        ];

        var time = rnd.NextSingle() * 48f * 60f;

        float amountRaw = Math.Abs(Gaussian(rnd)) * (fraud ? 160f + rnd.NextSingle() * 220f : 18f + rnd.NextSingle() * 140f);

        return new FraudTrainingObservation
        {
            Time = time,
            V = v,
            Amount = amountRaw,
            Label = fraud
        };
    }

    private static float Gaussian(Random rnd)
    {
        // Box-Muller (single draw)
        var u = 1.0 - rnd.NextDouble();
        var v = 1.0 - rnd.NextDouble();
        return (float)(Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v));
    }
}
