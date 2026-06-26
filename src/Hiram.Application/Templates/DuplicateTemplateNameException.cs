namespace Hiram.Application.Templates;

public sealed class DuplicateTemplateNameException : Exception
{
    public DuplicateTemplateNameException()
        : base("A template with the same name already exists for this channel.")
    {
    }
}
