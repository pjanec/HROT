using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common;

namespace Hrot.Editor;

/// <summary>
/// ⭐ N-6 (CE-4468; 📄 docs/DESIGN_Threaded_Work_Inventory.md §1, D-5) — the editor logic as the UI THREAD of the threaded editor sees it: the real
/// <see cref="EditorApplication"/> lives on the SIM side (over S); every verb is POSTED to the sim thread and runs there unchanged; reads pass
/// through (whole references / scalars). Built only by the threaded editor (<c>HROT_THREADED_KERNEL=3</c>).
/// </summary>
public sealed class ThreadedEditorLogic : IEditorLogic
{
    private readonly IEditorLogic _inner;
    private readonly Action<string, Action> _post;

    public ThreadedEditorLogic(IEditorLogic inner, Action<string, Action> post)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _post = post ?? throw new ArgumentNullException(nameof(post));
    }

    /// <summary>The sim side updates the real logic; the UI's call is a no-op.</summary>
    public void Update() { }
    public void NewScenario() => _post("editor: new scenario", _inner.NewScenario);
    public void SaveScenario(string filePath) => _post("editor: save scenario", () => _inner.SaveScenario(filePath));
    public void LoadScenarioByName(string scenarioName) => _post("editor: load scenario", () => _inner.LoadScenarioByName(scenarioName));
    /// <summary>⚠ <paramref name="then"/> runs on the SIM thread after the load was requested (as it ran after the call before).</summary>
    public void LoadScenarioByName(string scenarioName, Action then) => _post("editor: load scenario, then", () => _inner.LoadScenarioByName(scenarioName, then));
    public void CreateBlankScenario(string scenarioName, string terrainName) => _post("editor: create blank", () => _inner.CreateBlankScenario(scenarioName, terrainName));
    public Hrot.UI.Common.RunMode.ScenarioDoor? Door => _inner.Door;
    public string? SaveDisabledReason => _inner.SaveDisabledReason;
    public void SaveCurrentScenario() => _post("editor: save current", _inner.SaveCurrentScenario);
    public void SaveScenarioAs(string scenarioName) => _post("editor: save as", () => _inner.SaveScenarioAs(scenarioName));
    public string? LoadedScenarioName => _inner.LoadedScenarioName;
    public IReadOnlyList<string> AvailableScenarios => _inner.AvailableScenarios;
    public void ActivateTool(EditorTool tool) => _post("editor: activate tool", () => _inner.ActivateTool(tool));
    public void CommitPropertyEdit(int networkId, IReadOnlyList<object> updatedComponents) => _post("editor: commit property edit", () => _inner.CommitPropertyEdit(networkId, updatedComponents));
    public void CenterOnEntity(int entityId) => _post("editor: center on entity", () => _inner.CenterOnEntity(entityId));
    public void SelectEntity(int entityId) => _post("editor: select entity", () => _inner.SelectEntity(entityId));
    public void OpenRenameDialog(int entityId) => _post("editor: open rename dialog", () => _inner.OpenRenameDialog(entityId));
    public void RebuildAndReloadAI() => _post("editor: rebuild AI", _inner.RebuildAndReloadAI);
    public Task<AiReloadResult> RebuildAndReloadAIAsync()
    {
        var tcs = new TaskCompletionSource<AiReloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _post("editor: rebuild AI (async)", () => _inner.RebuildAndReloadAIAsync().ContinueWith(t =>
        {
            if (t.IsFaulted) tcs.TrySetException(t.Exception!.InnerExceptions);
            else if (t.IsCanceled) tcs.TrySetCanceled();
            else tcs.TrySetResult(t.Result);
        }, TaskScheduler.Default));
        return tcs.Task;
    }
    public bool IsScenarioDegraded => _inner.IsScenarioDegraded;
    public IReadOnlyList<SidecarFileInfo> GetMigrationSidecarsForCurrentScenario() => _inner.GetMigrationSidecarsForCurrentScenario();
}
