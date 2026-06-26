using Hiram.Domain.Notifications;
using Hiram.Domain.Templates;

namespace Hiram.UnitTests.Templates;

public class TemplateTests
{
    private static Template CreateValid() => new(
        id: Guid.NewGuid(),
        tenantId: Guid.NewGuid(),
        channel: NotificationChannel.Email,
        name: "welcome",
        subject: "Hi {{ name }}",
        body: "Welcome {{ name }}",
        createdAtUtc: DateTimeOffset.UnixEpoch);

    [Fact]
    public void Constructor_StoresValues_AndStampsUpdatedEqualToCreated()
    {
        var template = CreateValid();

        Assert.Equal(NotificationChannel.Email, template.Channel);
        Assert.Equal("welcome", template.Name);
        Assert.Equal("Hi {{ name }}", template.Subject);
        Assert.Equal(template.CreatedAtUtc, template.UpdatedAtUtc);
    }

    [Fact]
    public void Constructor_Throws_WhenTenantIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new Template(
            Guid.NewGuid(), Guid.Empty, NotificationChannel.Email, "n", "s", "b", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenChannelIsUnknown()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Template(
            Guid.NewGuid(), Guid.NewGuid(), (NotificationChannel)999, "n", "s", "b", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenNameIsBlank(string name)
    {
        Assert.Throws<ArgumentException>(() => new Template(
            Guid.NewGuid(), Guid.NewGuid(), NotificationChannel.Email, name, "s", "b", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenBodyIsBlank(string body)
    {
        Assert.Throws<ArgumentException>(() => new Template(
            Guid.NewGuid(), Guid.NewGuid(), NotificationChannel.Email, "n", "s", body, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void UpdateContent_ReplacesSubjectAndBody_AndStampsUpdated()
    {
        var template = CreateValid();
        var updatedAt = DateTimeOffset.UnixEpoch.AddDays(1);

        template.UpdateContent("New {{ name }}", "Body {{ name }}", updatedAt);

        Assert.Equal("New {{ name }}", template.Subject);
        Assert.Equal("Body {{ name }}", template.Body);
        Assert.Equal(updatedAt, template.UpdatedAtUtc);
        Assert.Equal(DateTimeOffset.UnixEpoch, template.CreatedAtUtc);
    }

    [Fact]
    public void UpdateContent_Throws_WhenSubjectIsBlank()
    {
        var template = CreateValid();

        Assert.Throws<ArgumentException>(() => template.UpdateContent("  ", "body", DateTimeOffset.UnixEpoch));
    }
}
