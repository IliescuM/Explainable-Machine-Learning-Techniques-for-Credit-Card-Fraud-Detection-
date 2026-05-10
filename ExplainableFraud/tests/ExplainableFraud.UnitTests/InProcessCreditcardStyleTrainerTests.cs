using ExplainableFraud.Contracts.Training;
using ExplainableFraud.Infrastructure.Ml;

namespace ExplainableFraud.UnitTests;

public sealed class InProcessCreditcardStyleTrainerTests
{
    [Theory]
    [InlineData(TrainingModelFamily.LogisticRegressionBaseline)]
    [InlineData(TrainingModelFamily.FastTreeGradientBoost)]
    [InlineData(TrainingModelFamily.FastForestEnsemble)]
    public void Fit_and_evaluate_returns_metrics_for_each_trainer_family(TrainingModelFamily family)
    {
        var rows = SyntheticCreditcardStyleDataset.Generate(4096, seed: 2025, fraudFraction: 0.06f).ToArray();

        var result = InProcessCreditcardStyleTrainer.FitAndEvaluate(family, rows, mlSeed: 7331);

        Assert.NotNull(result.Metrics);
        Assert.InRange(result.Metrics.AreaUnderRocCurve, 0d, 1.01);
        Assert.True(result.Metrics.TrainRows + result.Metrics.ValidationRows + result.Metrics.TestRows >= 3900);
    }
}
