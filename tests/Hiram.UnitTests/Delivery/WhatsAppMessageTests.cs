using Hiram.Application.Delivery;

namespace Hiram.UnitTests.Delivery;

public class WhatsAppMessageTests
{
    [Fact]
    public void Canonical_DiffersBetweenFreeFormAndTemplate_ForTheSameRecipient()
    {
        var freeForm = new WhatsAppMessage.FreeForm("+5511982254398", "order_shipped");
        var template = new WhatsAppMessage.Template("+5511982254398", "order_shipped", "pt_BR", []);

        // A body that happens to read like a template name is still a different message to the provider:
        // one goes out as written, the other asks Meta to render wording it approved.
        Assert.NotEqual(freeForm.Canonical, template.Canonical);
    }

    [Fact]
    public void Canonical_DiffersByLanguage_ForTheSameTemplateAndParameters()
    {
        var portuguese = new WhatsAppMessage.Template("+5511982254398", "order_shipped", "pt_BR", ["42"]);
        var english = new WhatsAppMessage.Template("+5511982254398", "order_shipped", "en_US", ["42"]);

        Assert.NotEqual(portuguese.Canonical, english.Canonical);
    }

    [Fact]
    public void Canonical_TellsOneParameterWithALineBreakFromTwoParameters()
    {
        var single = new WhatsAppMessage.Template("+5511982254398", "order_shipped", "pt_BR", ["42\n7"]);
        var pair = new WhatsAppMessage.Template("+5511982254398", "order_shipped", "pt_BR", ["42", "7"]);

        // This is the case the parameter count exists for. Joining by a newline alone would make these two
        // the same string, and the pipeline would drop one of them as a duplicate of the other.
        Assert.NotEqual(single.Canonical, pair.Canonical);
    }

    [Fact]
    public void Canonical_IsStable_ForTheSameFreeFormMessage()
    {
        var first = new WhatsAppMessage.FreeForm("+5511982254398", "Seu pedido 42 saiu para entrega.");
        var second = new WhatsAppMessage.FreeForm("+5511982254398", "Seu pedido 42 saiu para entrega.");

        Assert.Equal(first.Canonical, second.Canonical);
    }
}
