using Hiram.Application.Templates;
using Hiram.Infrastructure.Templates;

namespace Hiram.IntegrationTests.Templates;

public class ScribanTemplateRendererTests
{
    private readonly ITemplateRenderer _renderer = new ScribanTemplateRenderer();

    [Fact]
    public void Render_SubstitutesVariables()
    {
        var result = _renderer.Render("Hi {{ name }}", new Dictionary<string, object?> { ["name"] = "John" });

        Assert.Equal("Hi John", result);
    }

    [Fact]
    public void Render_Throws_OnMissingVariable()
    {
        Assert.Throws<TemplateRenderException>(() =>
            _renderer.Render("Hi {{ name }}", new Dictionary<string, object?>()));
    }

    [Fact]
    public void Render_SupportsConditional_WithBoolData()
    {
        var result = _renderer.Render(
            "{{ if vip }}VIP{{ else }}standard{{ end }}",
            new Dictionary<string, object?> { ["vip"] = true });

        Assert.Equal("VIP", result);
    }

    [Fact]
    public void TryValidate_ReturnsFalse_OnSyntaxError()
    {
        var ok = _renderer.TryValidate("{{ if vip }}no closing end", out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryValidate_ReturnsTrue_OnValidTemplate()
    {
        Assert.True(_renderer.TryValidate("Hi {{ name }}", out var error));
        Assert.Null(error);
    }
}
