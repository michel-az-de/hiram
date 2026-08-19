using Hiram.Simulator.Twilio;

namespace Hiram.Simulator;

public enum SimulatorCommand
{
    Walkthrough,
    Serve
}

public sealed record SimulatorOptions(
    SimulatorCommand Command,
    Uri DoubleAddress,
    Uri HiramAddress,
    string AdminKey,
    ProviderScenario Scenario,
    bool Live)
{
    private const string DefaultDouble = "http://localhost:4010/";
    private const string DefaultHiram = "http://localhost:3357/";

    public static SimulatorOptions Parse(string[] args)
    {
        var command = SimulatorCommand.Walkthrough;
        var doubleAddress = DefaultDouble;
        var hiramAddress = DefaultHiram;
        var adminKey = Environment.GetEnvironmentVariable("HIRAM_ADMIN_KEY") ?? "admin-dev-local";
        var scenario = ProviderScenario.Accept;
        var live = false;

        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            switch (argument)
            {
                case "serve":
                    command = SimulatorCommand.Serve;
                    break;
                case "walkthrough":
                    command = SimulatorCommand.Walkthrough;
                    break;
                case "--double":
                    doubleAddress = Next(args, ref i, argument);
                    break;
                case "--hiram":
                    hiramAddress = Next(args, ref i, argument);
                    break;
                case "--admin-key":
                    adminKey = Next(args, ref i, argument);
                    break;
                case "--scenario":
                    var requested = Next(args, ref i, argument);
                    if (!ProviderScenarios.TryParse(requested, out scenario))
                        throw new ArgumentException($"Unknown scenario '{requested}'. Try {ProviderScenarios.Codes}.");
                    break;
                case "--live":
                    live = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{argument}'.");
            }
        }

        return new SimulatorOptions(
            command,
            Absolute(doubleAddress, "--double"),
            Absolute(hiramAddress, "--hiram"),
            adminKey,
            scenario,
            live);
    }

    public static string Usage =>
        $"""
        Hiram.Simulator [serve|walkthrough] [options]

          serve         run only the provider double
          walkthrough   run the double and drive a delivery end to end (default)

          --double <url>      where the double listens (default http://localhost:4010/)
          --hiram <url>       the Hiram under test (default http://localhost:3357/)
          --admin-key <key>   X-Admin-Key, or the HIRAM_ADMIN_KEY environment variable
          --scenario <name>   {ProviderScenarios.Codes}
          --live              talk to the real provider instead of the double, and spend real money
        """;

    private static string Next(string[] args, ref int index, string argument)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{argument} needs a value.");

        return args[++index];
    }

    private static Uri Absolute(string value, string argument) =>
        Uri.TryCreate(value, UriKind.Absolute, out var address)
            ? address
            : throw new ArgumentException($"{argument} needs an absolute URL, and '{value}' is not one.");
}
