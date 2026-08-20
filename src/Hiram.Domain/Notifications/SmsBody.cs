using System.Text;

namespace Hiram.Domain.Notifications;

// An SMS is billed by segment, and the alphabet decides how big a segment is. Every character of GSM-7
// costs one septet and 160 of them fit in a lone message; one character outside that alphabet moves the
// whole message to UCS-2, where the limit drops to 70. In Portuguese that is the common case rather than
// the exception, because the tilde and the circumflex vowels are absent from GSM-7 while the acute e and
// the cedilla are present. A sentence that reads the same can cost three times as much.
public sealed record SmsBody
{
    private const int GsmSingle = 160;
    private const int GsmConcatenated = 153;
    private const int UnicodeSingle = 70;
    private const int UnicodeConcatenated = 67;

    // GSM 03.38 default alphabet. The escape at 0x1B is deliberately absent: it prefixes the extension
    // table below rather than being writable on its own.
    private const string GsmBasic =
        "@£$¥èéùìòÇ\nØø\rÅå"
        + "Δ_ΦΓΛΩΠΨΣΘΞÆæßÉ"
        + " !\"#¤%&'()*+,-./0123456789:;<=>?"
        + "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§"
        + "¿abcdefghijklmnopqrstuvwxyzäöñüà";

    // Each of these travels escaped, so it costs two septets instead of one.
    private const string GsmExtended = "\f^{}\\[~]|€";

    private static readonly HashSet<char> Basic = [.. GsmBasic];
    private static readonly HashSet<char> Extended = [.. GsmExtended];

    private SmsBody(string text, bool isUnicode, int segments)
    {
        Text = text;
        IsUnicode = isUnicode;
        Segments = segments;
    }

    // The normalised text, which is what should be handed to the provider: counting segments on one string
    // and sending another would report a cost the invoice does not agree with.
    public string Text { get; }

    public bool IsUnicode { get; }

    public int Segments { get; }

    public static SmsBody From(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("An SMS body is required.", nameof(text));

        var normalised = NormaliseQuotes(text);
        var septets = CountSeptets(normalised);

        return septets is null
            ? new SmsBody(normalised, true, Split(normalised.Length, UnicodeSingle, UnicodeConcatenated))
            : new SmsBody(normalised, false, Split(septets.Value, GsmSingle, GsmConcatenated));
    }

    // A curly quote pasted out of a word processor is typing noise, not intent, and on its own it doubles
    // the bill. It is the only edit made to a tenant's text: an accent carries meaning and stays, even
    // though removing it would be cheaper.
    private static string NormaliseQuotes(string text)
    {
        var normalised = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            normalised.Append(character switch
            {
                '‘' or '’' or '‚' or '‛' => '\'',
                '“' or '”' or '„' or '‟' => '"',
                _ => character
            });
        }

        return normalised.ToString();
    }

    // Null when a character falls outside GSM-7, which is what forces the whole message to UCS-2.
    private static int? CountSeptets(string text)
    {
        var septets = 0;
        foreach (var character in text)
        {
            if (Basic.Contains(character))
                septets++;
            else if (Extended.Contains(character))
                septets += 2;
            else
                return null;
        }

        return septets;
    }

    private static int Split(int length, int single, int concatenated) =>
        length <= single ? 1 : (length + concatenated - 1) / concatenated;
}
