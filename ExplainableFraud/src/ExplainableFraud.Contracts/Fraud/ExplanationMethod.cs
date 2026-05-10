using System.Text.Json.Serialization;

namespace ExplainableFraud.Contracts.Fraud;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExplanationMethod
{
    Unknown = 0,
    HeuristicWeights = 1,
    MetadataWeightedDeviation = 2
}
