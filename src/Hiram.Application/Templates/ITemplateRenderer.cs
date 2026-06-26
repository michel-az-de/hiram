namespace Hiram.Application.Templates;

public interface ITemplateRenderer
{
    // Renders the template with the given data. Throws TemplateRenderException on a syntax error or an
    // undefined variable, so a bad template or missing data surfaces as a 400 instead of a half built message.
    string Render(string template, IReadOnlyDictionary<string, object?> data);

    bool TryValidate(string template, out string? error);
}
