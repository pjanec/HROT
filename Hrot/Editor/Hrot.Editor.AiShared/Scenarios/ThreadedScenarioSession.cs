using System;
using System.Collections.Generic;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐ N-6 (CE-4468; 📄 docs/DESIGN_Threaded_Work_Inventory.md §1, D-5) — the scenario session as the UI THREAD of a threaded node sees it. The real
/// session lives on the SIM side (over the simulation world S: it clears it, saves it, pumps loads onto the orchestration bus). Every verb the UI
/// calls is POSTED to the sim thread (<paramref name="post"/> — the node's sim job queue) and runs there, unchanged; every read passes through
/// (a reference or a scalar the sim side replaces whole — a benign read; ⛔ no collection is enumerated here).
/// ⭐ Single-threaded hosts never build it: the UI holds the real session (unchanged).
/// </summary>
public sealed class ThreadedScenarioSession : IScenarioSession
{
    private readonly IScenarioSession _inner;
    private readonly Action<string, Action> _post;

    /// <param name="inner">The real session (sim side).</param>
    /// <param name="post">Posts a named action to the sim thread.</param>
    public ThreadedScenarioSession(IScenarioSession inner, Action<string, Action> post)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _post = post ?? throw new ArgumentNullException(nameof(post));
    }

    /// <summary>The real session (for the sim side).</summary>
    public IScenarioSession Inner => _inner;

    public string? LoadedScenarioName => _inner.LoadedScenarioName;
    public void OpenForEdit(string scenarioName) => _post("scenario: open for edit", () => _inner.OpenForEdit(scenarioName));
    public void LoadScenario(string scenarioName) => _post("scenario: load", () => _inner.LoadScenario(scenarioName));
    public void LoadForLive(string scenarioName) => _post("scenario: load live", () => _inner.LoadForLive(scenarioName));
    public void OpenRecording(Guid exerciseId) => _post("scenario: open recording", () => _inner.OpenRecording(exerciseId));
    public void OpenRecording(Guid exerciseId, double? atSimSeconds) => _post("scenario: open recording at", () => _inner.OpenRecording(exerciseId, atSimSeconds));
    public void SaveCurrent() => _post("scenario: save", _inner.SaveCurrent);
    public string? SaveDisabledReason => _inner.SaveDisabledReason;
    public void TakeCheckpoint() => _post("scenario: checkpoint", _inner.TakeCheckpoint);
    /// <summary>The sim side pumps the real session; the UI's call is a no-op.</summary>
    public void Update() { }
    public ClusterState CurrentClusterState => _inner.CurrentClusterState;
    public void ClearWorld() => _post("scenario: clear world", _inner.ClearWorld);
    public void Close() => _post("scenario: close", _inner.Close);
    public void RestoreCheckpoint(string label) => _post("scenario: restore checkpoint", () => _inner.RestoreCheckpoint(label));
    public void SaveAs(string scenarioName) => _post("scenario: save as", () => _inner.SaveAs(scenarioName));
    public void SaveSelectionAs(string scenarioName, Guid[] onlyGuids, string? headerJson) => _post("scenario: save selection as", () => _inner.SaveSelectionAs(scenarioName, onlyGuids, headerJson));
    public void CreateBlank(string scenarioName, string terrainName) => _post("scenario: create blank", () => _inner.CreateBlank(scenarioName, terrainName));
    public void SaveTo(string filePath) => _post("scenario: save to", () => _inner.SaveTo(filePath));
    public bool IsDegraded => _inner.IsDegraded;
    public IReadOnlyList<SidecarFileInfo> GetMigrationSidecars() => _inner.GetMigrationSidecars();
}
