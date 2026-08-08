using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Blocks;
using Hiram.Application.Consents;
using Hiram.Application.Events;
using Hiram.Application.Notifications;
using Hiram.Application.Routines;
using Hiram.Application.Templates;
using Hiram.Domain.Blocks;
using Hiram.Domain.Consents;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;
using Hiram.Domain.Routines;
using Hiram.Domain.Templates;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Templates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hiram.IntegrationTests.Events;

// Fakes rather than containers: what is under test is the routing decision and the pair of rows it
// produces, and the store is the seam where that becomes observable.
public class EventFanoutTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private const string Phone = "+5511982254398";
    private const string Email = "ops@example.com";

    private sealed class RecordingStore : INotificationStore
    {
        public List<(NotificationRequest Request, OutboxMessage Outbox)> Saved { get; } = [];
        public bool RejectDuplicates { get; set; }

        public Task SaveAsync(NotificationRequest request, OutboxMessage outbox, CancellationToken cancellationToken)
        {
            if (RejectDuplicates && Saved.Any(saved => saved.Request.IdempotencyKey == request.IdempotencyKey))
                throw new DuplicateIdempotencyKeyException();

            Saved.Add((request, outbox));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRoutineCatalog(params Routine[] routines) : IRoutineCatalog
    {
        public Task<IReadOnlyList<Routine>> MatchAsync(Guid tenantId, string eventType, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Routine>>(routines.Where(r => r.EventType == eventType).ToList());
    }

    private sealed class ApprovedTemplates(params Template[] templates) : ITemplateApprovalLookup, ITemplateStore
    {
        public Task<TemplateApproval> ForAsync(Guid tenantId, string templateName, NotificationChannel channel, CancellationToken cancellationToken)
        {
            var template = Find(templateName, channel);
            return Task.FromResult(template is null
                ? new TemplateApproval(Exists: false, Approved: false)
                : new TemplateApproval(Exists: true, template.Approved));
        }

        public Task<Template?> FindByNameAsync(Guid tenantId, NotificationChannel channel, string name, CancellationToken cancellationToken) =>
            Task.FromResult(Find(name, channel));

        private Template? Find(string name, NotificationChannel channel) =>
            templates.FirstOrDefault(t => t.Name == name && t.Channel == channel);

        public Task AddAsync(Template template, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Template?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Template>> ListAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Guid tenantId, Guid id, string? subject, string body, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ApproveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoConsentRecords : IConsentStore
    {
        public Task<Consent?> GetAsync(Guid tenantId, Guid userId, NotificationChannel channel, NotificationCategory category, CancellationToken cancellationToken) =>
            Task.FromResult<Consent?>(null);

        public Task UpsertAsync(Consent consent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoBlocks : IBlockStore
    {
        public Task<IReadOnlyList<Block>> ActiveBlocksAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Block>>([]);

        public Task AddAsync(Block block, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> RemoveAsync(Guid tenantId, Guid id, DateTimeOffset removedAtUtc, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private static Template SmsTemplate(string name = "entrega") =>
        Approved(new Template(Guid.NewGuid(), Tenant, NotificationChannel.Sms, name, subject: null, "Ola {{ name }}, seu pedido saiu", Now));

    private static Template EmailTemplate(string name = "entrega") =>
        Approved(new Template(Guid.NewGuid(), Tenant, NotificationChannel.Email, name, "Pedido de {{ name }}", "Ola {{ name }}", Now));

    private static Template Approved(Template template)
    {
        template.Approve();
        return template;
    }

    private static Routine Routine(NotificationCategory category, params NotificationChannel[] channels) =>
        new(Guid.NewGuid(), Tenant, "pedido_enviado", "entrega", channels, category, active: true);

    private static OutboxEventPayload Event(string? phone = Phone, string? email = Email, string eventId = "evt-1") =>
        new(
            Guid.NewGuid(),
            Tenant,
            "pedido_enviado",
            eventId,
            EmissionSeq: 1,
            new EventPayload(
                RecipientUserId: null,
                RecipientEmail: email,
                RecipientPhone: phone,
                LogicalAlertId: null,
                Timezone: null,
                new Dictionary<string, object?> { ["name"] = "Ada" }));

    private static EventFanout Fanout(RecordingStore store, Routine routine, params Template[] templates)
    {
        var catalog = new FakeRoutineCatalog(routine);
        var approvals = new ApprovedTemplates(templates);

        return new EventFanout(
            new RoutineResolver(catalog, approvals),
            new ChannelResolver(new ConsentPolicy(new NoConsentRecords()), new BlockGate(new NoBlocks())),
            approvals,
            new ScribanTemplateRenderer(),
            store,
            new FixedClock(),
            NullLogger<EventFanout>.Instance);
    }

    [Fact]
    public async Task SmsRoute_WritesTheRequestAndAnSmsOutboxRow()
    {
        var store = new RecordingStore();
        var fanout = Fanout(store, Routine(NotificationCategory.Transactional, NotificationChannel.Sms), SmsTemplate());

        await fanout.FanOutAsync(Event(), CancellationToken.None);

        var (request, outbox) = Assert.Single(store.Saved);
        Assert.Equal(NotificationChannel.Sms, request.Channel);
        Assert.Equal(Phone, request.Recipient);
        Assert.Null(request.Subject);
        Assert.Equal("Ola Ada, seu pedido saiu", request.Body);

        // The outbox type is the routing key the dispatcher switches on, so it decides which channel
        // actually sends. Getting it wrong would poison the message instead of delivering it.
        Assert.Equal("sms", outbox.Type);
        Assert.Equal(Tenant, outbox.TenantId);
    }

    [Fact]
    public async Task SmsRoute_WithoutAPhone_WritesNothing()
    {
        var store = new RecordingStore();
        var fanout = Fanout(store, Routine(NotificationCategory.Transactional, NotificationChannel.Sms), SmsTemplate());

        await fanout.FanOutAsync(Event(phone: null), CancellationToken.None);

        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task SmsRoute_WithAPhoneThatIsNotE164_WritesNothing()
    {
        var store = new RecordingStore();
        var fanout = Fanout(store, Routine(NotificationCategory.Transactional, NotificationChannel.Sms), SmsTemplate());

        // A carrier refuses this, so the row would only ever become a dead letter.
        await fanout.FanOutAsync(Event(phone: "11982254398"), CancellationToken.None);

        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task SmsRoute_Replayed_ResolvesToTheSameMessageKey()
    {
        var store = new RecordingStore();
        var fanout = Fanout(store, Routine(NotificationCategory.Transactional, NotificationChannel.Sms), SmsTemplate());

        await fanout.FanOutAsync(Event(), CancellationToken.None);
        var first = store.Saved.Single().Request.IdempotencyKey;

        // The key is a hash of event id, channel, recipient and template version, so a redelivery has to
        // land on the same value for the unique index to collapse it.
        store.RejectDuplicates = true;
        await fanout.FanOutAsync(Event(), CancellationToken.None);

        Assert.Single(store.Saved);
        Assert.Equal(first, store.Saved.Single().Request.IdempotencyKey);
        Assert.False(string.IsNullOrWhiteSpace(first));
    }

    [Fact]
    public async Task SmsRoute_Marketing_WithoutOptIn_IsSuppressed()
    {
        var store = new RecordingStore();
        var fanout = Fanout(store, Routine(NotificationCategory.Marketing, NotificationChannel.Sms), SmsTemplate());

        // Marketing has no legitimate-interest default, and opening the sms surfaces did not change that.
        await fanout.FanOutAsync(Event(), CancellationToken.None);

        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task EmailRoute_IsUnchanged_WhenSmsIsWiredAlongsideIt()
    {
        var store = new RecordingStore();
        var routine = Routine(NotificationCategory.Transactional, NotificationChannel.Email, NotificationChannel.Sms);
        var fanout = Fanout(store, routine, EmailTemplate(), SmsTemplate());

        await fanout.FanOutAsync(Event(), CancellationToken.None);

        Assert.Equal(2, store.Saved.Count);

        var email = store.Saved.Single(saved => saved.Request.Channel == NotificationChannel.Email);
        Assert.Equal(Email, email.Request.Recipient);
        Assert.Equal("Pedido de Ada", email.Request.Subject);
        Assert.Equal("Ola Ada", email.Request.Body);
        Assert.Equal("email", email.Outbox.Type);

        var sms = store.Saved.Single(saved => saved.Request.Channel == NotificationChannel.Sms);
        Assert.Null(sms.Request.Subject);
        Assert.Equal("sms", sms.Outbox.Type);

        // One event, two channels, two distinct message keys: neither collapses onto the other.
        Assert.NotEqual(email.Request.IdempotencyKey, sms.Request.IdempotencyKey);
    }

    [Fact]
    public async Task UnapprovedSmsTemplate_IsSuppressed_NotSent()
    {
        var store = new RecordingStore();
        var unapproved = new Template(Guid.NewGuid(), Tenant, NotificationChannel.Sms, "entrega", subject: null, "Corpo", Now);
        var fanout = Fanout(store, Routine(NotificationCategory.Transactional, NotificationChannel.Sms), unapproved);

        await fanout.FanOutAsync(Event(), CancellationToken.None);

        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task SmsOutboxPayload_CarriesTheRenderedBodyAndNoSubject()
    {
        var store = new RecordingStore();
        var fanout = Fanout(store, Routine(NotificationCategory.Transactional, NotificationChannel.Sms), SmsTemplate());

        await fanout.FanOutAsync(Event(), CancellationToken.None);

        // The worker reads the payload verbatim, so what is serialized here is what a replay reproduces.
        var payload = JsonSerializer.Deserialize<OutboxNotificationPayload>(store.Saved.Single().Outbox.Payload)!;
        Assert.Equal(NotificationChannel.Sms.ToString(), payload.Channel);
        Assert.Equal(Phone, payload.Recipient);
        Assert.Null(payload.Subject);
        Assert.Equal("Ola Ada, seu pedido saiu", payload.Body);
    }
}
