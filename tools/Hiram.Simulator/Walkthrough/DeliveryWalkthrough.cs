using Hiram.Contracts;
using Hiram.Simulator.Providers;

namespace Hiram.Simulator.Walkthrough;

// Three acts against a running Hiram: a delivery that succeeds, a delivery the provider refuses, and the
// same path reached through the event fan-out. Together they cover what a stubbed handler cannot, which is
// the outbox, the worker, the durable claim, the attempt record and the dead letter.
public sealed class DeliveryWalkthrough
{
    private const string Destination = "+5511999990000";

    private static readonly TimeSpan SettlementBudget = TimeSpan.FromSeconds(30);

    private readonly HiramApi _hiram;
    private readonly IProviderDouble? _double;
    private readonly Uri _doubleAddress;
    private readonly Transcript _transcript;
    private readonly ProviderScenario _refusal;

    public DeliveryWalkthrough(
        HiramApi hiram, IProviderDouble? provider, Uri doubleAddress, Transcript transcript, ProviderScenario refusal)
    {
        _hiram = hiram;
        _double = provider;
        _doubleAddress = doubleAddress;
        _transcript = transcript;
        _refusal = refusal;
    }

    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        if (!await _hiram.IsReadyAsync(cancellationToken))
        {
            _transcript.Problem(
                "Hiram is not answering health/ready. Start it first, for example with "
                + "docker compose -f docker-compose.dev.yml up.");
            return false;
        }

        var tenant = await ProvisionAsync(cancellationToken);
        var accepted = await DeliverAsync("act 1, the provider accepts", ProviderScenario.Accept, cancellationToken);
        var refused = await DeliverAsync("act 2, the provider refuses", _refusal, cancellationToken);
        var routed = await FanOutAsync(tenant, cancellationToken);

        if (_double is not null)
        {
            _transcript.Section("what the double saw");
            foreach (var line in _double.Log)
                _transcript.Detail(line);
        }

        _transcript.Section("summary");
        _transcript.Row("direct send, accepting provider", accepted);
        _transcript.Row("direct send, refusing provider", refused);
        _transcript.Row("event fan-out", routed);

        // A permanent refusal that never reaches a dead letter means the classification is wrong, so the
        // run fails: the point of act 2 is proving the bad path lands somewhere with a name on it. A
        // transient refusal is judged only on not having been reported as delivered, because the retry
        // budget is longer than the walkthrough waits.
        var refusalHeld = _refusal is ProviderScenario.RateLimited or ProviderScenario.ServerError
            ? refused != "sent"
            : refused == "dead_lettered";

        return accepted == "sent" && refusalHeld && routed == "sent";
    }

    private async Task<Guid> ProvisionAsync(CancellationToken cancellationToken)
    {
        _transcript.Section("provisioning");

        var tenant = await _hiram.CreateTenantAsync($"simulator-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}", cancellationToken);
        _transcript.Detail($"tenant {tenant}, live delivery mode");

        await _hiram.UseApiKeyAsync(tenant, "simulator", cancellationToken);
        _transcript.Detail("api key issued, held in memory only");

        // Placeholders on purpose: the double never checks the secret, and a walkthrough that needed a real
        // credential would stop being runnable offline.
        // Which channels exist and what each one is called comes from the double, so pointing the run at
        // another provider does not mean editing the script.
        foreach (var config in Configs)
            await _hiram.SetProviderAsync(
                config.Channel,
                new SetProviderConfigRequest(config.Provider, new Dictionary<string, string>(config.Settings), "simulator"),
                cancellationToken);

        _transcript.Detail(
            $"{string.Join(" and ", Configs.Select(config => config.Channel))} configured, provider host is {_doubleAddress}");
        return tenant;
    }

    private async Task<string> DeliverAsync(string title, ProviderScenario scenario, CancellationToken cancellationToken)
    {
        _transcript.Section(title);
        SetScenario(scenario);
        _transcript.Detail($"double answering {ProviderScenarios.Describe(scenario)}");

        var id = await _hiram.SubmitNotificationAsync(
            new SubmitNotificationRequest(Channel, Destination, Body: "Seu pedido 42 saiu para entrega."),
            cancellationToken);
        _transcript.Detail($"accepted as {id}");

        var detail = await _hiram.AwaitSettlementAsync(id, SettlementBudget, cancellationToken);
        Report(detail);
        return detail.Status;
    }

    private async Task<string> FanOutAsync(Guid tenant, CancellationToken cancellationToken)
    {
        _transcript.Section("act 3, the event fan-out");
        SetScenario(ProviderScenario.Accept);

        const string templateName = "simulator-order-shipped";
        const string eventType = "SimulatorOrderShipped";

        await _hiram.EnsureTemplateAsync(
            new CreateTemplateRequest(Channel, templateName, null, "Pedido {{ order }} saiu para entrega."),
            cancellationToken);
        await _hiram.CreateRoutineAsync(tenant, eventType, templateName, [Channel], cancellationToken);

        var user = Guid.NewGuid();
        await _hiram.SetConsentAsync(new SetConsentRequest(user, Channel, "transactional", true), cancellationToken);
        _transcript.Detail($"template, routine and opt-in ready for user {user}");

        var before = await KnownIdsAsync(cancellationToken);
        await _hiram.SubmitEventAsync(
            new SubmitEventRequest(
                eventType,
                Guid.NewGuid().ToString("n"),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                new EventRecipient(user.ToString(), null, Destination),
                Data: new Dictionary<string, object?> { ["order"] = 42 }),
            cancellationToken);

        var id = await AwaitNewNotificationAsync(before, cancellationToken);
        if (id is null)
        {
            _transcript.Problem("the event was accepted and no notification appeared, so no routine matched it");
            return "no_route";
        }

        var detail = await _hiram.AwaitSettlementAsync(id.Value, SettlementBudget, cancellationToken);
        Report(detail);
        return detail.Status;
    }

    private void SetScenario(ProviderScenario scenario)
    {
        if (_double is not null)
            _double.Scenario = scenario;
    }

    // Live mode has no double to ask, and the values it would provide are the ones a real account already
    // has configured, so there is nothing to provision and nothing to name.
    private IReadOnlyList<ProviderConfig> Configs => _double?.Configs ?? [];

    private string Channel => _double?.WalkthroughChannel ?? "sms";

    private async Task<HashSet<Guid>> KnownIdsAsync(CancellationToken cancellationToken) =>
        (await _hiram.ListNotificationsAsync(Channel, cancellationToken)).Select(item => item.Id).ToHashSet();

    private async Task<Guid?> AwaitNewNotificationAsync(HashSet<Guid> before, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + SettlementBudget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var fresh = (await _hiram.ListNotificationsAsync(Channel, cancellationToken))
                .FirstOrDefault(item => !before.Contains(item.Id));
            if (fresh is not null)
                return fresh.Id;

            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        }

        return null;
    }

    private void Report(NotificationDetailResponse detail)
    {
        _transcript.Row("status", detail.Status);
        foreach (var attempt in detail.Attempts)
            _transcript.Row($"attempt {attempt.AttemptNumber}", $"{attempt.Provider}, {attempt.Outcome}{Reason(attempt.Error)}");

        if (detail.DeadLetter is { } dead)
            _transcript.Row("dead letter", $"{dead.Reason} after {dead.AttemptCount} attempts");
    }

    private static string Reason(string? error) =>
        string.IsNullOrWhiteSpace(error) ? string.Empty : $", {error}";
}
