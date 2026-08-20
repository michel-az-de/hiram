using Hiram.Domain.Notifications;

namespace Hiram.UnitTests.Notifications;

// The carrier bills by segment, and one character outside GSM-7 moves the whole message to UCS-2, where a
// segment holds 67 characters instead of 153. In Portuguese that is not an edge case: "nao" written with
// the tilde triples the bill of a message that would otherwise fit in one segment.
public class SmsBodyTests
{
    [Fact]
    public void PlainText_UnderTheGsmLimit_IsOneSegment()
    {
        var body = SmsBody.From(new string('a', 160));

        Assert.False(body.IsUnicode);
        Assert.Equal(1, body.Segments);
    }

    [Fact]
    public void PlainText_OverTheGsmLimit_SplitsAtOneHundredFiftyThree()
    {
        var body = SmsBody.From(new string('a', 161));

        Assert.False(body.IsUnicode);
        Assert.Equal(2, body.Segments);
    }

    [Fact]
    public void AccentsInsideGsm_KeepTheMessageCheap()
    {
        // é, è, à, ç, ñ, ö and ü are in the GSM-7 basic set.
        var body = SmsBody.From("Seu café chegou, obrigado");

        Assert.False(body.IsUnicode);
    }

    [Fact]
    public void AccentsOutsideGsm_MoveTheWholeMessage()
    {
        // The tilde and the circumflex vowels are not, so most Portuguese sentences fall out of GSM-7 on
        // a single character and the limit drops from 160 to 70.
        Assert.True(SmsBody.From("Seu pedido não pode ser entregue").IsUnicode);
        Assert.True(SmsBody.From("O pagamento está pendente").IsUnicode);
        Assert.True(SmsBody.From("Confira o número do pedido").IsUnicode);
    }

    [Fact]
    public void OneHundredFortyEightCharacters_CostThreeSegmentsWithDiacritics_AndOneWithout()
    {
        var expensive = SmsBody.From(new string('á', 148));
        var cheap = SmsBody.From(new string('a', 148));

        Assert.Equal(3, expensive.Segments);
        Assert.Equal(1, cheap.Segments);
    }

    [Fact]
    public void ExtensionCharacters_CostTwoSeptetsEach()
    {
        // The braces, the brackets, the backslash, the caret, the tilde, the pipe and the euro sign are on
        // the GSM-7 extension table, where each one is escaped and therefore costs two.
        var full = SmsBody.From(new string('{', 80));
        var overflowing = SmsBody.From(new string('{', 81));

        Assert.False(full.IsUnicode);
        Assert.Equal(1, full.Segments);
        Assert.Equal(2, overflowing.Segments);
    }

    [Fact]
    public void CurlyQuotes_AreNormalised_AndNothingElseIs()
    {
        var body = SmsBody.From("O “pedido” do João ‘chegou’");

        Assert.Equal("O \"pedido\" do João 'chegou'", body.Text);
    }

    [Fact]
    public void NormalisingQuotes_CanBringAMessageBackIntoGsm()
    {
        // A quote pasted from a word processor is typing noise, not intent, and on its own it doubled the
        // bill. Normalising it is the one edit the product makes to a tenant's text.
        var body = SmsBody.From("O “pedido” chegou");

        Assert.False(body.IsUnicode);
        Assert.Equal(1, body.Segments);
    }

    [Fact]
    public void EmptyBody_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => SmsBody.From("   "));
    }
}
