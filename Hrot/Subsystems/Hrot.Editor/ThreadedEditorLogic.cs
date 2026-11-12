using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common;
using Hrot.Editor.AiShared.Scenarios;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.Common.Events;
using Hrot.ScenarioEditor.Services;

namespace Hrot.Editor;

/// <summary>
/// ⭐ N-6 (CE-4468; D-7 / D-9) — the editor logic as the UI THREAD of the threaded editor sees it (<c>HROT_THREADED_KERNEL=3</c>). The real
/// <see cref="EditorApplication"/> lives on the SIM side (over S).
/// <list type="bullet">
/// <item>scenario verbs and reads → the UI's <see cref="ThreadedScenarioSession"/> (its door asks on the UI thread; the raw verbs are sim jobs;
/// reads come from the readout the sim publishes);</item>
/// <item>the editor's UI commands (tool, select, centre, rename, a property commit) are EVENTS → published on the UI world's own bus, exactly as
/// the real logic publishes them on its world's; the staging carries them to S between ticks (⛔ never a publish on a sim-side bus);</item>
/// <item>the C# behaviour rebuild is already off-thread (a build process + a swap the node frame drains) → the real logic's.</item>
/// </list>
/// </summary>
public sealed class ThreadedEditorLogic : IEditorLogic
{
    private readonly EditorApplication _inner;
    private readonly ThreadedScenarioSession _session;
    private readonly FdpEventBus _uiBus;

    public ThreadedEditorLogic(EditorApplication inner, ThreadedScenarioSession session, FdpEventBus uiBus)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _uiBus = uiBus ?? throw new ArgumentNullException(nameof(uiBus));
    }

    /// <summary>The sim side updates the real logic; the UI's call is a no-op.</summary>
    public void Update() { }

    // ── scenario: the UI session ──
    public void NewScenario() => _session.ClearWorld();
    public void SaveScenario(string filePath) => _session.SaveTo(filePath);
    public void LoadScenarioByName(string scenarioName) => _session.OpenForEdit(scenarioName);
    public void LoadScenarioByName(string scenarioName, Action then) => _session.Door.Open(scenarioName, then: then);
    public void CreateBlankScenario(string scenarioName, string terrainName) => _session.CreateBlank(scenarioName, terrainName);
    public Hrot.UI.Common.RunMode.ScenarioDoor? Door => _session.Door;
    public string? SaveDisabledReason => _session.SaveDisabledReason;
    public void SaveCurrentScenario() => _session.SaveCurrent();
    public void SaveScenarioAs(string scenarioName) => _session.SaveAs(scenarioName);
    public string? LoadedScenarioName => _session.LoadedScenarioName;
    /// <summary>The scenario folder's listing (a file enumeration — no sim state).</summary>
    public IReadOnlyList<string> AvailableScenarios => _inner.AvailableScenarios;
    public bool IsScenarioDegraded => _session.IsDegraded;
    public IReadOnlyList<SidecarFileInfo> GetMigrationSidecarsForCurrentScenario() => _session.GetMigrationSidecars();

    // ── the editor's UI commands: events on the UI world's bus ──
    public void ActivateTool(EditorTool tool) => _uiBus.Publish(new ActivateEditorToolEvent(tool));
    public void CommitPropertyEdit(int networkId, IReadOnlyList<object> updatedComponents)
    {
        if (updatedComponents == null) throw new ArgumentNullException(nameof(updatedComponents));
        _uiBus.PublishManaged(new UpdateEntityCommand { NetworkId = networkId, ComponentsToUpdate = new List<object>(updatedComponents) });
    }
    public void CenterOnEntity(int entityId) => _uiBus.Publish(new CenterOnEntityCommand { NetworkId = entityId });
    public void SelectEntity(int entityId) => _uiBus.Publish(new SelectEntityCommand { NetworkId = entityId });
    public void OpenRenameDialog(int entityId) => _uiBus.Publish(new OpenRenameDialogCommand { NetworkId = entityId });

    // ── the C# behaviour rebuild (already off the frame) ──
    public void RebuildAndReloadAI() => _inner.RebuildAndReloadAI();
    public Task<AiReloadResult> RebuildAndReloadAIAsync() => _inner.RebuildAndReloadAIAsync();
}
