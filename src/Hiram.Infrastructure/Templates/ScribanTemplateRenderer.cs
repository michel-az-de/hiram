using System.Text.Json;
using Hiram.Application.Templates;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Hiram.Infrastructure.Templates;

public sealed class ScribanTemplateRenderer : ITemplateRenderer
{
    public string Render(string template, IReadOnlyDictionary<string, object?> data)
    {
        var parsed = Template.Parse(template);
        if (parsed.HasErrors)
            throw new TemplateRenderException(Describe(parsed));

        var scriptObject = new ScriptObject();
        foreach (var pair in data)
            scriptObject[pair.Key] = Normalize(pair.Value);

        var context = new TemplateContext { StrictVariables = true };
        context.PushGlobal(scriptObject);

        try
        {
            return parsed.Render(context);
        }
        catch (ScriptRuntimeException ex)
        {
            throw new TemplateRenderException(ex.OriginalMessage);
        }
    }

    public bool TryValidate(string template, out string? error)
    {
        var parsed = Template.Parse(template);
        if (parsed.HasErrors)
        {
            error = Describe(parsed);
            return false;
        }

        error = null;
        return true;
    }

    // Request data arrives as JsonElement, so unwrap it to the primitive Scriban understands, letting
    // conditionals and numbers behave instead of every value rendering as raw JSON text.
    private static object? Normalize(object? value) => value switch
    {
        JsonElement element => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var number) ? number : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        },
        _ => value
    };

    private static string Describe(Template template) =>
        string.Join("; ", template.Messages.Select(message => message.Message));
}
