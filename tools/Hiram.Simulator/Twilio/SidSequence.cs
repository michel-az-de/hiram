using System.Globalization;

namespace Hiram.Simulator.Twilio;

// Identifiers a run can be compared against the previous one. A random SID would make two executions of
// the same script produce logs that cannot be diffed, which is most of the value of a local double.
public sealed class SidSequence
{
    private readonly string _prefix;
    private int _issued;

    public SidSequence(string prefix)
    {
        if (prefix.Length != 2)
            throw new ArgumentException("A Twilio SID prefix is two characters, such as SM or EM.", nameof(prefix));

        _prefix = prefix;
    }

    public int Issued => Volatile.Read(ref _issued);

    public string Next()
    {
        var ordinal = Interlocked.Increment(ref _issued);
        return _prefix + ordinal.ToString("x32", CultureInfo.InvariantCulture);
    }
}
