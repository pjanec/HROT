using System;
using Bagira.SimHost;
using Bagira.SimHost.Utilities;

//  Bagira.SimHost entry point 
// Opens a graphical 2-D window (Raylib + ImGui) with road/vehicle visualization
// while running the full network-distributed simulation kernel via CycloneDDS.
// All initialization logic lives in SimHostApp : FdpApplication.

try
{
    using var app = new SimHostApp();
    app.Run();
}
catch (Exception ex)
{
    Logger.Error($"[SimHost] Fatal: {ex}");
    Console.Error.WriteLine(ex);
    Environment.Exit(1);
}
