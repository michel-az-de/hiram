namespace Hiram.Application.Delivery;

// Explicit result of a send attempt. Each provider adapter classifies its own errors into transient
// (worth retrying) or permanent (fail fast), so the pipeline never inspects provider specific details.
public abstract record SendOutcome
{
    private SendOutcome()
    {
    }

    public sealed record Sent : SendOutcome;

    public sealed record TransientFailure(string Reason) : SendOutcome;

    public sealed record PermanentFailure(string Reason) : SendOutcome;
}
