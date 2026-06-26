namespace Hiram.Application.Webhooks;

public sealed class DuplicateWebhookEndpointException : Exception
{
    public DuplicateWebhookEndpointException()
        : base("This url is already registered as a webhook for the tenant.")
    {
    }
}
