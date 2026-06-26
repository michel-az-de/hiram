namespace Hiram.Contracts;

public sealed record CreateTemplateRequest(string Channel, string Name, string Subject, string Body);
