using Hiram.Application.Delivery;
using Polly;
using Polly.Retry;

namespace Hiram.Infrastructure.Delivery;

public static class EmailDeliveryPipeline
{
    // 3 attempts for a transient failure, exponential backoff with jitter on a 2s base. Permanent
    // failures are not handled here, so they fall through on the first attempt.
    public static ResiliencePipeline<SendOutcome> Build() =>
        new ResiliencePipelineBuilder<SendOutcome>()
            .AddRetry(new RetryStrategyOptions<SendOutcome>
            {
                ShouldHandle = new PredicateBuilder<SendOutcome>().HandleResult(outcome => outcome is SendOutcome.TransientFailure),
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2),
                UseJitter = true
            })
            .Build();
}
