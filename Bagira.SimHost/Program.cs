using System;
using Bagira.SimHost;
using Bagira.SimHost.Utilities;

//  Bagira.SimHost entry point 
// Opens a graphical 2-D window (Raylib + ImGui) with road/vehicle visualization
// while running the full network-distributed simulation kernel via CycloneDDS.
// All initialization logic lives in SimHostApp : FdpApplication.

// ── CLI: --domain / -d ────────────────────────────────────────────────────
// Priority: CLI arg > config.json DomainId > hardcoded default (0)
int domainId = 0;
for (int i = 0; i < args.Length - 1; i++)
{
    if ((args[i] == "--domain" || args[i] == "-d") && int.TryParse(args[i + 1], out int d))
    {
        domainId = d;
        break;
    }
}

Console.WriteLine($"[SimHost] Starting — domain={domainId}");

try
{
    using var app = new SimHostApp(domainId != 0 ? domainId : null);
    app.Run();
}
catch (Exception ex)
{
    Logger.Error($"[SimHost] Fatal: {ex}");
    Console.Error.WriteLine(ex);
    Environment.Exit(1);
}
