using Hiram.Domain.Outbox;

namespace Hiram.UnitTests.Outbox;

public class OutboxMessageTests
{
    private static OutboxMessage CreateValid() => new(
        id: Guid.NewGuid(),
        tenantId: Guid.NewGuid(),
        type: "email",
        payload: "{}",
        createdAtUtc: DateTimeOffset.UnixEpoch);

    [Fact]
    public void Constructor_LeavesMessageUnprocessed()
    {
        var message = CreateValid();

        Assert.Null(message.ProcessedAtUtc);
        Assert.False(message.IsProcessed);
    }

    [Fact]
    public void Constructor_Throws_WhenTenantIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new OutboxMessage(
            Guid.NewGuid(), Guid.Empty, "email", "{}", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenTypeIsBlank(string type)
    {
        Assert.Throws<ArgumentException>(() => new OutboxMessage(
            Guid.NewGuid(), Guid.NewGuid(), type, "{}", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenPayloadIsBlank(string payload)
    {
        Assert.Throws<ArgumentException>(() => new OutboxMessage(
            Guid.NewGuid(), Guid.NewGuid(), "email", payload, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void MarkProcessed_SetsProcessedTimestamp()
    {
        var message = CreateValid();
        var processedAt = DateTimeOffset.UnixEpoch.AddMinutes(5);

        message.MarkProcessed(processedAt);

        Assert.Equal(processedAt, message.ProcessedAtUtc);
        Assert.True(message.IsProcessed);
    }

    [Fact]
    public void MarkProcessed_Throws_WhenAlreadyProcessed()
    {
        var message = CreateValid();
        message.MarkProcessed(DateTimeOffset.UnixEpoch.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(() => message.MarkProcessed(DateTimeOffset.UnixEpoch.AddMinutes(6)));
    }
}
