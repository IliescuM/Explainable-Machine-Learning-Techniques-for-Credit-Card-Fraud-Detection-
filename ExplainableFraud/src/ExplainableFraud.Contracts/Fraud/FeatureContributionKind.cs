using System.Text.Json.Serialization;

namespace ExplainableFraud.Contracts.Fraud;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeatureContributionKind
{
    Unknown = 0,
    Scalar = 1,
    PrincipalComponent = 2,
    PrincipalComponentAggregate = 3
}
