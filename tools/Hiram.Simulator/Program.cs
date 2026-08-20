using Hiram.Simulator.Twilio;
using Hiram.Simulator.Walkthrough;

namespace Hiram.Simulator;

// A named entry point instead of top level statements: the test project references both this tool and
// Hiram.Api, and two assemblies with top level statements would each emit a global Program type.
internal static class EntryPoint
{
    private static async Task<int> Main(string[] args)
    {
        SimulatorOptions options;
        try
        {
            options = SimulatorOptions.Parse(args);
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(SimulatorOptions.Usage);
            return 2;
        }

        using var stopping = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopping.Cancel();
        };

        // In live mode there is no double: the Hiram under test keeps its production endpoints and the
        // money is real. The mode exists so the same script can confirm a paid account, never as default.
        var twilio = options.Live ? null : new TwilioDouble(options.Scenario);
        var host = twilio?.Build(options.DoubleAddress);

        if (host is not null)
        {
            await host.StartAsync(stopping.Token);
            Console.WriteLine($"provider double listening on {options.DoubleAddress}");
            Console.WriteLine($"point Hiram at it with Hiram__Providers__Endpoints__TwilioApi={options.DoubleAddress}");
            Console.WriteLine($"and Hiram__Providers__Endpoints__TwilioEmail={new Uri(options.DoubleAddress, "v1/")}");
        }
        else
        {
            Console.WriteLine("live mode: no double, the real provider will be called and charged");
        }

        try
        {
            if (options.Command is SimulatorCommand.Serve)
            {
                Console.WriteLine("serving until Ctrl+C");
                await WaitForCancellationAsync(stopping.Token);
                return 0;
            }

            // In the walkthrough the scenario names how act 2 should be refused, since acts 1 and 3 have to
            // succeed for the run to prove anything. Without one, the refusal is an opt-out reply.
            var refusal = options.Scenario is ProviderScenario.Accept
                ? ProviderScenario.RecipientOptedOut
                : options.Scenario;

            using var hiram = new HiramApi(options.HiramAddress, options.AdminKey);
            var walkthrough = new DeliveryWalkthrough(hiram, twilio, options.DoubleAddress, new Transcript(), refusal);
            return await walkthrough.RunAsync(stopping.Token) ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        finally
        {
            if (host is not null)
            {
                await host.StopAsync(CancellationToken.None);
                await host.DisposeAsync();
            }
        }
    }

    private static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var stopped = new TaskCompletionSource();
        await using var registration = cancellationToken.Register(stopped.SetResult);
        await stopped.Task;
    }
}
