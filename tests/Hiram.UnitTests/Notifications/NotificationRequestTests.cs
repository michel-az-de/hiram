using Hiram.Domain.Notifications;

namespace Hiram.UnitTests.Notifications;

public class NotificationRequestTests
{
    private static NotificationRequest CreateValid() => new(
        id: Guid.NewGuid(),
        tenantId: Guid.NewGuid(),
        channel: NotificationChannel.Email,
        recipient: "felipe@example.com",
        subject: "hello",
        body: "first slice",
        createdAtUtc: DateTimeOffset.UnixEpoch);

    [Fact]
    public void Constructor_StartsInAcceptedStatus()
    {
        var request = CreateValid();

        Assert.Equal(NotificationStatus.Accepted, request.Status);
    }

    [Fact]
    public void Constructor_StoresProvidedValues()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UnixEpoch;

        var request = new NotificationRequest(id, tenantId, NotificationChannel.Email, "to@example.com", "subject", "body", createdAt);

        Assert.Equal(id, request.Id);
        Assert.Equal(tenantId, request.TenantId);
        Assert.Equal(NotificationChannel.Email, request.Channel);
        Assert.Equal("to@example.com", request.Recipient);
        Assert.Equal("subject", request.Subject);
        Assert.Equal("body", request.Body);
        Assert.Equal(createdAt, request.CreatedAtUtc);
    }

    [Fact]
    public void Constructor_Throws_WhenIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new NotificationRequest(
            Guid.Empty, Guid.NewGuid(), NotificationChannel.Email, "to@example.com", "s", "b", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenTenantIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new NotificationRequest(
            Guid.NewGuid(), Guid.Empty, NotificationChannel.Email, "to@example.com", "s", "b", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenChannelIsUnknown()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationRequest(
            Guid.NewGuid(), Guid.NewGuid(), (NotificationChannel)999, "to@example.com", "s", "b", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenRecipientIsBlank(string recipient)
    {
        Assert.Throws<ArgumentException>(() => new NotificationRequest(
            Guid.NewGuid(), Guid.NewGuid(), NotificationChannel.Email, recipient, "s", "b", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenSubjectIsBlank(string subject)
    {
        Assert.Throws<ArgumentException>(() => new NotificationRequest(
            Guid.NewGuid(), Guid.NewGuid(), NotificationChannel.Email, "to@example.com", subject, "b", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenBodyIsBlank(string body)
    {
        Assert.Throws<ArgumentException>(() => new NotificationRequest(
            Guid.NewGuid(), Guid.NewGuid(), NotificationChannel.Email, "to@example.com", "s", body, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void MarkSent_TransitionsFromAcceptedToSent()
    {
        var request = CreateValid();

        request.MarkSent();

        Assert.Equal(NotificationStatus.Sent, request.Status);
    }

    [Fact]
    public void MarkSent_IsIdempotent()
    {
        var request = CreateValid();

        request.MarkSent();
        request.MarkSent();

        Assert.Equal(NotificationStatus.Sent, request.Status);
    }

    [Fact]
    public void Constructor_KeepsIdempotencyKey_WhenProvided()
    {
        var request = new NotificationRequest(
            Guid.NewGuid(), Guid.NewGuid(), NotificationChannel.Email, "to@example.com", "s", "b",
            DateTimeOffset.UnixEpoch, idempotencyKey: "evt-0001");

        Assert.Equal("evt-0001", request.IdempotencyKey);
    }

    [Fact]
    public void Constructor_LeavesIdempotencyKeyNull_WhenOmitted()
    {
        Assert.Null(CreateValid().IdempotencyKey);
    }

    [Fact]
    public void MarkDeadLettered_TransitionsFromSending()
    {
        var request = CreateValid();
        request.MarkSending();

        request.MarkDeadLettered();

        Assert.Equal(NotificationStatus.DeadLettered, request.Status);
    }

    [Fact]
    public void MarkDeadLettered_TransitionsFromFailed()
    {
        var request = CreateValid();
        request.MarkFailed();

        request.MarkDeadLettered();

        Assert.Equal(NotificationStatus.DeadLettered, request.Status);
    }

    [Fact]
    public void MarkDeadLettered_Throws_WhenStillAccepted()
    {
        var request = CreateValid();

        Assert.Throws<InvalidOperationException>(request.MarkDeadLettered);
    }

    [Fact]
    public void RequeueForReplay_TransitionsFromDeadLetteredToQueued()
    {
        var request = CreateValid();
        request.MarkSending();
        request.MarkDeadLettered();

        request.RequeueForReplay();

        Assert.Equal(NotificationStatus.Queued, request.Status);
    }

    [Fact]
    public void RequeueForReplay_Throws_WhenNotDeadLettered()
    {
        var request = CreateValid();
        request.MarkSending();

        Assert.Throws<InvalidOperationException>(request.RequeueForReplay);
    }
}
