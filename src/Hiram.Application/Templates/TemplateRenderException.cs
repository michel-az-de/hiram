namespace Hiram.Application.Templates;

public sealed class TemplateRenderException : Exception
{
    public TemplateRenderException(string message) : base(message)
    {
    }
}
