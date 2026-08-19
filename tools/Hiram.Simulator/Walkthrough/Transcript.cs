namespace Hiram.Simulator.Walkthrough;

// The run's own report. It writes a fixed width label so two runs can be diffed line by line, which is
// most of what a deterministic double buys.
public sealed class Transcript
{
    private const int LabelWidth = 34;

    public void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine(new string('-', title.Length));
    }

    public void Detail(string line) => Console.WriteLine($"  {line}");

    public void Row(string label, string value) => Console.WriteLine($"  {label.PadRight(LabelWidth)}{value}");

    public void Problem(string line)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"  {line}");
    }
}
