using Bagira.Map.Common;
using Bagira.Orchestrator;
using CycloneDDS.Runtime;

namespace Bagira.Orchestrator.Standalone;

internal static class Program
{
    private static int Main(string[] args)
    {
        var domain = 0;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if ((args[i] == "-d" || args[i] == "--domain") && int.TryParse(args[i + 1], out var d))
                domain = d;
        }

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        Console.WriteLine($"[Orchestrator.Standalone] Domain {domain}. Ctrl+C to exit.");

        using var participant = BagiraEnvironment.CreateParticipant(domain);
        using var drill = new DrillMaster(participant);

        while (!cancel.IsCancellationRequested)
        {
            drill.Tick();
            Thread.Sleep(1);
        }

        Console.WriteLine("[Orchestrator.Standalone] Shutdown.");
        return 0;
    }
}
