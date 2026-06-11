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
    public void MarkPublished_TransitionsFromAcceptedToPublished()
    {
        var request = CreateValid();

        request.MarkPublished();

        Assert.Equal(NotificationStatus.Published, request.Status);
    }

    [Fact]
    public void MarkPublished_IsIdempotent()
    {
        var request = CreateValid();

        request.MarkPublished();
        request.MarkPublished();

        Assert.Equal(NotificationStatus.Published, request.Status);
    }
}
