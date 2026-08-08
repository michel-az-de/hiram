using System.Text.RegularExpressions;

namespace Hiram.Domain.Notifications;

public static class PhoneNumber
{
    // E.164: a plus sign, a country code that cannot start at zero, and at most 15 digits overall. A
    // carrier rejects anything else, so catching it before the outbox keeps a guaranteed failure out of
    // the queue, whether the number arrived on a direct submit or inside an event payload.
    private static readonly Regex E164 = new(@"^\+[1-9]\d{7,14}$", RegexOptions.Compiled);

    public static bool IsE164(string? number) =>
        !string.IsNullOrWhiteSpace(number) && E164.IsMatch(number.Trim());
}
