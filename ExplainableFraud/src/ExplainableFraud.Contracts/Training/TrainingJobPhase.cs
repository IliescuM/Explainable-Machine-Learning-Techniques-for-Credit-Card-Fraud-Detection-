namespace ExplainableFraud.Contracts.Training;

public enum TrainingJobPhase
{
    NotStarted = 0,
    LoadingDataset = 1,
    ModelTraining = 2,
    Evaluating = 3,
    WritingArtifacts = 4
}
