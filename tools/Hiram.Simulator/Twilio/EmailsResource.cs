using System.Text.Json;
using System.Text.Json.Serialization;
using Hiram.Simulator.Providers;

namespace Hiram.Simulator.Twilio;

// The Email API answers asynchronously with an operation id, and rejects with either a top level message
// or a list of them. Both rejection shapes are produced here because the adapter reads both, and only one
// of them is exercised by the tests that exist today.
public static class EmailsResource
{
    public static ProviderResponse For(ProviderScenario scenario, string operationId) => scenario switch
    {
        ProviderScenario.Accept =>
            new(202, JsonSerializer.Serialize(new Accepted(operationId))),

        ProviderScenario.RateLimited =>
            new(429, JsonSerializer.Serialize(new Failure("Too Many Requests.", null))),

        ProviderScenario.ServerError =>
            new(500, JsonSerializer.Serialize(new Failure("Internal Server Error.", null))),

        // The rejections the Messages resource models by code all collapse to one shape here: the Email
        // API has no numeric code, so the reason has to survive in the text or the dead letter says nothing.
        _ => new(400, JsonSerializer.Serialize(
            new Failure(null, [new FailureDetail($"The Email API rejected the request ({ProviderScenarios.Describe(scenario)}).")])))
    };

    public static ProviderResponse Unauthorized() =>
        new(401, JsonSerializer.Serialize(new Failure("Authentication Error, no credentials provided.", null)));

    private sealed record Accepted(
        [property: JsonPropertyName("operationId")] string OperationId);

    private sealed record Failure(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("errors")] FailureDetail[]? Errors);

    private sealed record FailureDetail(
        [property: JsonPropertyName("message")] string Message);
}
