using System.Globalization;
using System.Text;

namespace Hiram.Simulator.Meta;

// Identifiers a run can be compared against the previous one, in the shape Meta issues: "wamid." followed
// by base64. A random one would make two executions of the same script produce logs that cannot be
// diffed, which is most of the value of a local double.
public sealed class WamidSequence
{
    private int _issued;

    public int Issued => Volatile.Read(ref _issued);

    public string Next()
    {
        var ordinal = Interlocked.Increment(ref _issued);
        var payload = $"HBgNSIMULATOR{ordinal.ToString("D6", CultureInfo.InvariantCulture)}";

        return "wamid." + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }
}
