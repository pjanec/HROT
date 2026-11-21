using System;
using System.Collections.Generic;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐ N-6 (CE-4468; D-7 / D-9 in STATUS_Overnight_Threaded_Build.md; 📄 docs/DESIGN_Threaded_Work_Inventory.md §1, D-5) — the scenario session as
/// the UI THREAD of a threaded node sees it. The real session lives on the SIM side (over S: it clears it, saves it, pumps loads onto the
/// orchestration bus).
/// <list type="bullet">
/// <item>⭐ every VERB the UI calls is posted to the sim thread and runs there, unchanged (the node's sim job queue);</item>
/// <item>⭐ every READ is answered from a <see cref="Readout"/> the sim computes after each tick and publishes with the staging (applied here on the
/// UI thread, in tick order) — and the cluster state from the UI's own cluster view (its mirror of the orchestration bus) — ⛔ never a read of a
/// sim-side object from the UI thread;</item>
/// <item>⭐ it owns the UI's <see cref="Door"/> (prompts belong to the UI thread); the sim session keeps its own door for the HTTP routes, which run
/// on the sim thread with a refusing asker.</item>
/// </list>
/// Single-threaded hosts never build it: the UI holds the real session (unchanged).
/// </summary>
public sealed class ThreadedScenarioSession : IScenarioSession, IRunScenarioControl, IHasScenarioDoor, ISaveInterception
{
    /// <summary>What the UI reads, computed on the sim thread after a tick (immutable; replaced whole).</summary>
    public sealed record Readout(string? LoadedScenarioName, string? SaveDisabledReason, bool IsDegraded,
                                 IReadOnlyList<SidecarFileInfo> Sidecars, string? LoadedArgsJson)
    {
        public static readonly Readout Empty = new(null, null, false, Array.Empty<SidecarFileInfo>(), null);
    }

    private readonly IScenarioSession _inner;
    private readonly IRunScenarioControl _innerRun;
    private readonly Action<string, Action> _post;
    private readonly Func<ClusterState> _clusterState;
    private Readout _readout = Readout.Empty;

    /// <param name="inner">The real session (sim side).</param>
    /// <param name="innerRun">Its run control (the raw verbs).</param>
    /// <param name="post">Posts a named action to the sim thread.</param>
    /// <param name="clusterState">The cluster state as the UI's own cluster view holds it.</param>
    /// <param name="leaveRaw">The real session's raw leave (run on the sim thread).</param>
    /// <param name="autosaveOf">The autosave reader (a file read — no sim state).</param>
    public ThreadedScenarioSession(IScenarioSession inner, IRunScenarioControl innerRun, Action<string, Action> post, Func<ClusterState> clusterState,
                                   Action<LeaveReason> leaveRaw, Func<string, AutosaveFiles.Status>? autosaveOf)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _innerRun = innerRun ?? throw new ArgumentNullException(nameof(innerRun));
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _clusterState = clusterState ?? throw new ArgumentNullException(nameof(clusterState));
        if (leaveRaw is null) throw new ArgumentNullException(nameof(leaveRaw));
        Door = new ScenarioDoor(this, () => ClusterReplayControls.ModeOf(_clusterState()), reason => _post("scenario: leave", () => leaveRaw(reason)))
        {
            AutosaveOf = autosaveOf,
        };
    }

    /// <summary>The UI's door (its asker is the UI's).</summary>
    public ScenarioDoor Door { get; }

    /// <summary>The sim side, after a tick: a NEW readout of the real session when anything the UI reads changed, else null (no allocation).
    /// The migration sidecars (a file read) are read again only when the loaded scenario changed.</summary>
    public static Readout? ReadIfChanged(Readout? last, IScenarioSession sim)
    {
        string? name = sim.LoadedScenarioName;
        string? save = sim.SaveDisabledReason;
        bool degraded = sim.IsDegraded;
        string? args = (sim as IRunScenarioControl)?.LoadedArgsJson;
        if (last != null && last.LoadedScenarioName == name && last.SaveDisabledReason == save && last.IsDegraded == degraded && last.LoadedArgsJson == args)
            return null;
        var sidecars = last != null && last.LoadedScenarioName == name ? last.Sidecars : sim.GetMigrationSidecars();
        return new Readout(name, save, degraded, sidecars, args);
    }

    /// <summary>The UI thread: the readout the sim published (in tick order).</summary>
    public void Apply(Readout readout) => _readout = readout ?? Readout.Empty;

    /// <summary>The readout the UI holds now.</summary>
    public Readout Current => _readout;

    // ── reads ──
    public string? LoadedScenarioName => _readout.LoadedScenarioName;
    public string? SaveDisabledReason => _readout.SaveDisabledReason;
    public ClusterState CurrentClusterState => _clusterState();
    public bool IsDegraded => _readout.IsDegraded;
    public IReadOnlyList<SidecarFileInfo> GetMigrationSidecars() => _readout.Sidecars;
    string? IRunScenarioControl.LoadedArgsJson => _readout.LoadedArgsJson;

    // ── the guarded verbs: the UI's door (it asks on the UI thread, then calls the raw verbs below) ──
    public void OpenForEdit(string scenarioName) => Door.Open(scenarioName);
    public void LoadScenario(string scenarioName) => Door.Load(scenarioName);
    public void LoadForLive(string scenarioName) => Door.LoadLive(scenarioName);
    public void OpenRecording(Guid exerciseId) => Door.OpenRecording(exerciseId);
    public void OpenRecording(Guid exerciseId, double? atSimSeconds) => Door.OpenRecording(exerciseId, atSimSeconds);
    /// <summary>The out door (Save / Discard / Cancel asked on the UI thread, then the raw leave posted).</summary>
    public void Close() => Door.Leave(LeaveReason.Close);

    /// <inheritdoc/> — set by the UI's run choice (a Live / DAR / AAR run takes a checkpoint instead); checked HERE, on the UI thread.
    public Func<bool>? SaveIntercept { get; set; }

    // ── the other verbs (posted to the real session) ──
    public void SaveCurrent() { if (SaveIntercept?.Invoke() == true) return; _post("scenario: save", _inner.SaveCurrent); }
    public void SaveAs(string scenarioName) { if (SaveIntercept?.Invoke() == true) return; _post("scenario: save as", () => _inner.SaveAs(scenarioName)); }
    public void TakeCheckpoint() => _post("scenario: checkpoint", _inner.TakeCheckpoint);
    /// <summary>The sim side pumps the real session; the UI's call is a no-op.</summary>
    public void Update() { }
    public void ClearWorld() => _post("scenario: clear world", _inner.ClearWorld);
    public void RestoreCheckpoint(string label) => _post("scenario: restore checkpoint", () => _inner.RestoreCheckpoint(label));
    public void SaveSelectionAs(string scenarioName, Guid[] onlyGuids, string? headerJson) => _post("scenario: save selection as", () => _inner.SaveSelectionAs(scenarioName, onlyGuids, headerJson));
    public void CreateBlank(string scenarioName, string terrainName) => _post("scenario: create blank", () => _inner.CreateBlank(scenarioName, terrainName));
    public void SaveTo(string filePath) => _post("scenario: save to", () => _inner.SaveTo(filePath));

    // ── the raw run verbs (the door and the run choice call these after they decided) — posted to the real session's raw verbs ──
    void IRunScenarioControl.OpenForEdit(string scenarioName) => _post("scenario: raw open for edit", () => _innerRun.OpenForEdit(scenarioName));
    void IRunScenarioControl.OpenForEdit(string scenarioName, string? argsJson) => _post("scenario: raw open for edit", () => _innerRun.OpenForEdit(scenarioName, argsJson));
    void IRunScenarioControl.OpenForEdit(string scenarioName, string? argsJson, bool recoverAutosave) => _post("scenario: raw open for edit (autosave)", () => _innerRun.OpenForEdit(scenarioName, argsJson, recoverAutosave));
    void IRunScenarioControl.LoadForLive(string scenarioName) => _post("scenario: raw load live", () => _innerRun.LoadForLive(scenarioName));
    void IRunScenarioControl.LoadForLive(string scenarioName, string? argsJson) => _post("scenario: raw load live", () => _innerRun.LoadForLive(scenarioName, argsJson));
    void IRunScenarioControl.OpenRecording(Guid exerciseId) => _post("scenario: raw open recording", () => _innerRun.OpenRecording(exerciseId));
    void IRunScenarioControl.OpenRecording(Guid exerciseId, double? atSimSeconds) => _post("scenario: raw open recording", () => _innerRun.OpenRecording(exerciseId, atSimSeconds));
    void IRunScenarioControl.SaveCurrent() => _post("scenario: raw save", _innerRun.SaveCurrent);
    void IRunScenarioControl.TakeCheckpoint() => _post("scenario: raw checkpoint", _innerRun.TakeCheckpoint);
}
