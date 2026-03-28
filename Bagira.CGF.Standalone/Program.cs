using System;
using System.Threading;
using Bagira.CGF;

namespace Bagira.CGF.Standalone;

internal static class Program
{
    private static int Main(string[] args)
    {
        var domain = 0;
        var nodeId = 400;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if ((args[i] == "-d" || args[i] == "--domain") && int.TryParse(args[i + 1], out var d))
                domain = d;
            if ((args[i] == "-n" || args[i] == "--node-id") && int.TryParse(args[i + 1], out var n))
                nodeId = n;
        }

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        Console.WriteLine($"[CGF.Standalone] Domain {domain}, NodeId {nodeId}. Ctrl+C to exit.");

        using var app = new CgfApplication(domain, nodeId);

        while (!cancel.IsCancellationRequested)
        {
            app.Tick();
            Thread.Sleep(16); // ~60 Hz
        }

        Console.WriteLine("[CGF.Standalone] Shutdown.");
        return 0;
    }
}
