using FDP.Toolkit.Scenario;
using Hrot.ScenarioEditor.Services;

namespace Hrot.Editor;

/// <summary>
/// Static factory for constructing the Editor's core services.
/// The full composition root (Raylib window, module kernel) lives in <c>Program.cs</c>
/// and is wired up in Phase 5 (PACK2-C001).
/// </summary>
public static class EditorBootstrap
{
    /// <summary>
    /// Root directory used for scenario files.
    /// Scenarios are stored as <c>{ScenariosRoot}\{scenarioName}\scenario.json</c>.
    /// </summary>
    public static string ScenariosRoot { get; } = @"C:\FDP_Temp";

    /// <summary>
    /// Builds a <see cref="ScenarioFileService"/> with an auto-serializer
    /// configured for <c>"Hrot.Scenario"</c> subsystem type.
    /// </summary>
    public static ScenarioFileService CreateFileService()
    {
        var serializer = new ScenarioSerializerBuilder("Hrot.Scenario")
            // No custom translators yet; FdpAutoSerializer handles all registered component types.
            .Build();

        return new ScenarioFileService(serializer);
    }
}
