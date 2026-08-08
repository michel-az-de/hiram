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

    private static Template CreateSms(string body = "Seu pedido saiu para entrega") => new(
        id: Guid.NewGuid(),
        tenantId: Guid.NewGuid(),
        channel: NotificationChannel.Sms,
        name: "entrega",
        subject: null,
        body: body,
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

    [Fact]
    public void Constructor_AcceptsANullSubject_OnSms()
    {
        var template = CreateSms();

        Assert.Null(template.Subject);
        Assert.Equal(NotificationChannel.Sms, template.Channel);
    }

    // The guard follows NotificationRequest.RequiresSubject, so relaxing it for SMS must not relax email.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenSubjectIsBlank_OnEmail(string? subject)
    {
        Assert.Throws<ArgumentException>(() => new Template(
            Guid.NewGuid(), Guid.NewGuid(), NotificationChannel.Email, "n", subject, "b", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void UpdateContent_AcceptsANullSubject_OnSms()
    {
        var template = CreateSms();
        var updatedAt = DateTimeOffset.UnixEpoch.AddDays(1);

        template.UpdateContent(subject: null, "Novo corpo", updatedAt);

        Assert.Null(template.Subject);
        Assert.Equal("Novo corpo", template.Body);
        Assert.Equal(2, template.Version);
        Assert.Equal(updatedAt, template.UpdatedAtUtc);
    }

    [Fact]
    public void Checksum_IsStable_WhenTheSubjectIsNull()
    {
        // A null subject must hash like any other content: the same body always yields the same checksum,
        // and changing the body changes it. Otherwise an SMS template would look edited on every read.
        var first = CreateSms();
        var second = CreateSms();
        Assert.Equal(first.Checksum, second.Checksum);

        first.UpdateContent(subject: null, "Outro corpo", DateTimeOffset.UnixEpoch.AddDays(1));
        Assert.NotEqual(second.Checksum, first.Checksum);
    }
}
