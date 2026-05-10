using System.ComponentModel.DataAnnotations;

namespace ExplainableFraud.Contracts.Fraud;

public sealed class FraudScoreRequest : IValidatableObject
{
    public const int PrincipalComponentCount = 28;

    public float Amount { get; init; }
    public float Time { get; init; }
    public float[]? PrincipalComponents { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!float.IsFinite(Amount) || Amount < 0)
            yield return new ValidationResult("Amount must be a finite non-negative value.", [nameof(Amount)]);

        if (!float.IsFinite(Time) || Time < 0)
            yield return new ValidationResult("Time must be a finite non-negative value.", [nameof(Time)]);

        if (PrincipalComponents is null)
            yield break;

        if (PrincipalComponents.Length > PrincipalComponentCount)
            yield return new ValidationResult($"Provide at most {PrincipalComponentCount} PCA values.", [nameof(PrincipalComponents)]);

        for (var i = 0; i < PrincipalComponents.Length; i++)
        {
            if (!float.IsFinite(PrincipalComponents[i]))
                yield return new ValidationResult($"PCA value V{i + 1} must be finite.", [nameof(PrincipalComponents)]);
        }
    }
}
