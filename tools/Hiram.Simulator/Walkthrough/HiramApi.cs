using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hiram.Contracts;

namespace Hiram.Simulator.Walkthrough;

// A client for the Hiram public API, and nothing else. The walkthrough never touches the database: what it
// proves has to be what an emitter would see, otherwise it proves a path no tenant can take.
public sealed class HiramApi : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _adminKey;

    private string? _apiKey;

    public HiramApi(Uri address, string adminKey)
    {
        _http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
        _adminKey = adminKey;
    }

    public void Dispose() => _http.Dispose();

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync("health/ready", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<Guid> CreateTenantAsync(string name, CancellationToken cancellationToken)
    {
        var created = await AdminAsync<TenantCreated>(
            HttpMethod.Post, "v1/admin/tenants", new { name, deliveryMode = "live" }, cancellationToken);

        return created.Id;
    }

    public async Task UseApiKeyAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        var created = await AdminAsync<ApiKeyCreated>(
            HttpMethod.Post, "v1/admin/api-keys", new { tenantId, name }, cancellationToken);

        _apiKey = created.Key;
    }

    public Task SetProviderAsync(string channel, SetProviderConfigRequest request, CancellationToken cancellationToken) =>
        TenantAsync(HttpMethod.Put, $"v1/providers/{channel}", request, cancellationToken);

    public async Task<Guid> EnsureTemplateAsync(CreateTemplateRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendTenantAsync(HttpMethod.Post, "v1/templates", request, cancellationToken);
        if (response.StatusCode is not HttpStatusCode.Conflict)
        {
            await EnsureSuccessAsync(response, "create template", cancellationToken);
            var created = await ReadAsync<TemplateResponse>(response, cancellationToken);
            await ApproveTemplateAsync(created.Id, cancellationToken);
            return created.Id;
        }

        // Re-running the walkthrough must converge instead of failing on the template it created last time.
        var existing = await FindTemplateAsync(request.Channel, request.Name, cancellationToken)
            ?? throw new InvalidOperationException($"Template {request.Name} answered 409 and is not in the listing.");

        await ApproveTemplateAsync(existing, cancellationToken);
        return existing;
    }

    public Task ApproveTemplateAsync(Guid id, CancellationToken cancellationToken) =>
        TenantAsync(HttpMethod.Post, $"v1/templates/{id}/approve", null, cancellationToken);

    public Task CreateRoutineAsync(
        Guid tenantId, string eventType, string templateName, IReadOnlyList<string> channels, CancellationToken cancellationToken) =>
        AdminAsync(
            HttpMethod.Post,
            "v1/admin/routines",
            new { tenantId, eventType, templateName, channels, category = "transactional", active = true },
            cancellationToken);

    public Task SetConsentAsync(SetConsentRequest request, CancellationToken cancellationToken) =>
        TenantAsync(HttpMethod.Post, "v1/consent", request, cancellationToken);

    public async Task<Guid> SubmitNotificationAsync(SubmitNotificationRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendTenantAsync(HttpMethod.Post, "v1/notifications", request, cancellationToken);
        await EnsureSuccessAsync(response, "submit notification", cancellationToken);
        return (await ReadAsync<NotificationAccepted>(response, cancellationToken)).Id;
    }

    public async Task<Guid> SubmitEventAsync(SubmitEventRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendTenantAsync(HttpMethod.Post, "v1/events", request, cancellationToken);
        await EnsureSuccessAsync(response, "submit event", cancellationToken);
        return (await ReadAsync<EventAccepted>(response, cancellationToken)).Id;
    }

    public async Task<IReadOnlyList<NotificationResponse>> ListNotificationsAsync(string channel, CancellationToken cancellationToken)
    {
        using var response = await SendTenantAsync(HttpMethod.Get, $"v1/notifications?channel={channel}&limit=50", null, cancellationToken);
        await EnsureSuccessAsync(response, "list notifications", cancellationToken);
        return (await ReadAsync<NotificationPage>(response, cancellationToken)).Items;
    }

    public async Task<NotificationDetailResponse> GetNotificationAsync(Guid id, CancellationToken cancellationToken)
    {
        using var response = await SendTenantAsync(HttpMethod.Get, $"v1/notifications/{id}", null, cancellationToken);
        await EnsureSuccessAsync(response, "read notification", cancellationToken);
        return await ReadAsync<NotificationDetailResponse>(response, cancellationToken);
    }

    // Polls until the notification settles or the budget runs out. Settled is what the delivery path calls
    // terminal, so a walkthrough that stops earlier would report a state the worker is still moving.
    public async Task<NotificationDetailResponse> AwaitSettlementAsync(
        Guid id, TimeSpan budget, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + budget;
        NotificationDetailResponse detail;
        do
        {
            detail = await GetNotificationAsync(id, cancellationToken);
            if (IsSettled(detail.Status))
                return detail;

            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return detail;
    }

    private static bool IsSettled(string status) =>
        status is "sent" or "failed" or "suppressed" or "dead_lettered";

    private async Task<Guid?> FindTemplateAsync(string channel, string name, CancellationToken cancellationToken)
    {
        using var response = await SendTenantAsync(HttpMethod.Get, "v1/templates", null, cancellationToken);
        await EnsureSuccessAsync(response, "list templates", cancellationToken);

        var templates = await ReadAsync<TemplateResponse[]>(response, cancellationToken);
        return templates
            .FirstOrDefault(template =>
                string.Equals(template.Name, name, StringComparison.Ordinal)
                && string.Equals(template.Channel, channel, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private async Task AdminAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, payload, admin: true, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);
    }

    private async Task<T> AdminAsync<T>(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, payload, admin: true, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task TenantAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        using var response = await SendTenantAsync(method, path, payload, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);
    }

    private Task<HttpResponseMessage> SendTenantAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken) =>
        SendAsync(method, path, payload, admin: false, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? payload, bool admin, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (payload is not null)
            request.Content = JsonContent.Create(payload, options: Json);

        if (admin)
            request.Headers.Add("X-Admin-Key", _adminKey);
        else
            request.Headers.Add("X-Api-Key", _apiKey ?? throw new InvalidOperationException("No API key has been issued yet."));

        return await _http.SendAsync(request, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
        ?? throw new InvalidOperationException($"Empty body where a {typeof(T).Name} was expected.");

    // The body is included because a validation problem names the offending field, and losing it turns
    // every failure into "the call did not work".
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"{what} answered {(int)response.StatusCode}: {body}");
    }

    private sealed record TenantCreated(Guid Id, string Name, string DeliveryMode);

    private sealed record ApiKeyCreated(Guid Id, Guid TenantId, string Name, string Key, string Prefix);
}
