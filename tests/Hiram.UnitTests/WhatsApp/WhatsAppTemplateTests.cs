using Hiram.Domain.WhatsApp;

namespace Hiram.UnitTests.WhatsApp;

public class WhatsAppTemplateTests
{
    private static WhatsAppTemplate CreateValid() => new(
        id: Guid.NewGuid(),
        tenantId: Guid.NewGuid(),
        name: "order-shipped",
        metaName: "order_shipped",
        language: "pt_BR",
        category: WhatsAppTemplateCategory.Utility,
        parameters: ["customerName", "trackingCode"],
        createdAtUtc: DateTimeOffset.UnixEpoch);

    [Fact]
    public void Constructor_StoresValues_StartsPending_AndStampsUpdatedEqualToCreated()
    {
        var template = CreateValid();

        Assert.Equal("order-shipped", template.Name);
        Assert.Equal("order_shipped", template.MetaName);
        Assert.Equal("pt_BR", template.Language);
        Assert.Equal(WhatsAppTemplateCategory.Utility, template.Category);
        Assert.Equal(WhatsAppTemplateStatus.Pending, template.Status);
        Assert.Equal(new[] { "customerName", "trackingCode" }, template.Parameters);
        Assert.Equal(template.CreatedAtUtc, template.UpdatedAtUtc);
        Assert.False(template.IsSendable);
    }

    [Fact]
    public void Constructor_Throws_WhenIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new WhatsAppTemplate(
            Guid.Empty, Guid.NewGuid(), "n", "m", "pt_BR", WhatsAppTemplateCategory.Utility, [], DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenTenantIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new WhatsAppTemplate(
            Guid.NewGuid(), Guid.Empty, "n", "m", "pt_BR", WhatsAppTemplateCategory.Utility, [], DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenNameIsBlank(string name)
    {
        Assert.Throws<ArgumentException>(() => new WhatsAppTemplate(
            Guid.NewGuid(), Guid.NewGuid(), name, "m", "pt_BR", WhatsAppTemplateCategory.Utility, [], DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenMetaNameIsBlank(string metaName)
    {
        Assert.Throws<ArgumentException>(() => new WhatsAppTemplate(
            Guid.NewGuid(), Guid.NewGuid(), "n", metaName, "pt_BR", WhatsAppTemplateCategory.Utility, [], DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenLanguageIsBlank(string language)
    {
        Assert.Throws<ArgumentException>(() => new WhatsAppTemplate(
            Guid.NewGuid(), Guid.NewGuid(), "n", "m", language, WhatsAppTemplateCategory.Utility, [], DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenCategoryIsUnknown()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WhatsAppTemplate(
            Guid.NewGuid(), Guid.NewGuid(), "n", "m", "pt_BR", (WhatsAppTemplateCategory)999, [], DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_Throws_WhenParametersIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new WhatsAppTemplate(
            Guid.NewGuid(), Guid.NewGuid(), "n", "m", "pt_BR", WhatsAppTemplateCategory.Utility, null!, DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenAParameterNameIsBlank(string blank)
    {
        Assert.Throws<ArgumentException>(() => new WhatsAppTemplate(
            Guid.NewGuid(), Guid.NewGuid(), "n", "m", "pt_BR", WhatsAppTemplateCategory.Utility, ["ok", blank], DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Constructor_AllowsEmptyParameters_ForATemplateWithNoPlaceholders()
    {
        var template = new WhatsAppTemplate(
            Guid.NewGuid(), Guid.NewGuid(), "shipped", "shipped", "pt_BR",
            WhatsAppTemplateCategory.Utility, [], DateTimeOffset.UnixEpoch);

        Assert.Empty(template.Parameters);
    }

    [Fact]
    public void Constructor_CopiesParameters_SoLaterMutationDoesNotLeak()
    {
        var source = new List<string> { "a", "b" };

        var template = new WhatsAppTemplate(
            Guid.NewGuid(), Guid.NewGuid(), "n", "m", "pt_BR", WhatsAppTemplateCategory.Utility, source, DateTimeOffset.UnixEpoch);
        source.Add("c");

        Assert.Equal(new[] { "a", "b" }, template.Parameters);
    }

    [Fact]
    public void RecordMetaStatus_ToApproved_MakesItSendable_AndStampsUpdated()
    {
        var template = CreateValid();
        var updatedAt = DateTimeOffset.UnixEpoch.AddDays(1);

        template.RecordMetaStatus(WhatsAppTemplateStatus.Approved, updatedAt);

        Assert.Equal(WhatsAppTemplateStatus.Approved, template.Status);
        Assert.True(template.IsSendable);
        Assert.Equal(updatedAt, template.UpdatedAtUtc);
    }

    [Fact]
    public void RecordMetaStatus_ToPaused_LeavesItNotSendable()
    {
        var template = CreateValid();
        template.RecordMetaStatus(WhatsAppTemplateStatus.Approved, DateTimeOffset.UnixEpoch.AddDays(1));

        template.RecordMetaStatus(WhatsAppTemplateStatus.Paused, DateTimeOffset.UnixEpoch.AddDays(2));

        Assert.False(template.IsSendable);
    }

    [Fact]
    public void RecordMetaStatus_Throws_WhenStatusIsUnknown()
    {
        var template = CreateValid();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            template.RecordMetaStatus((WhatsAppTemplateStatus)999, DateTimeOffset.UnixEpoch));
    }
}
