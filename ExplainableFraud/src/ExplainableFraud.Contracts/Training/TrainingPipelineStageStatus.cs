namespace ExplainableFraud.Contracts.Training;

public enum TrainingPipelineStageStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Skipped = 3,
    Failed = 4
}
