namespace ExplainableFraud.Web.Services;

public sealed class FraudApiException(int statusCode, string responseBody)
    : Exception($"API {statusCode}: {responseBody}")
{
    public int StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;
}
