using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Selection;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Windows;

// ── IHsmGlobalsCommandDispatcher ──────────────────────────────────────────────

/// <summary>
/// Seam that lets tests substitute the command dispatch for HsmGlobalsStrip.
/// Keeps the strip's interaction logic testable without ImGui.
/// </summary>
public interface IHsmGlobalsCommandDispatcher
{
    /// <summary>Remove the global transition with the given VisualId from the asset.</summary>
    void RemoveGlobalTransition(Guid visualId);
}

// ── HsmGlobalsStripLogic ───────────────────────────────────────────────────────

/// <summary>
/// Pure-logic layer for <see cref="HsmGlobalsStrip"/> — extracted so headless tests
/// can exercise it without an ImGui context.
/// </summary>
public sealed class HsmGlobalsStripLogic
{
    private readonly HsmAsset _asset;
    private readonly EditorSelectionStore _store;
    private readonly IHsmGlobalsCommandDispatcher _dispatcher;

    public HsmGlobalsStripLogic(
        HsmAsset asset,
        EditorSelectionStore store,
        IHsmGlobalsCommandDispatcher dispatcher)
    {
        _asset      = asset      ?? throw new ArgumentNullException(nameof(asset));
        _store      = store      ?? throw new ArgumentNullException(nameof(store));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>Returns summary labels ("Event→Target") for each global transition.</summary>
    public IReadOnlyList<string> GetChipLabels()
    {
        var labels = new List<string>(_asset.AllGlobalTransitions.Count);
        foreach (var g in _asset.AllGlobalTransitions)
        {
            var eventName = _asset.AllEvents
                                  .FirstOrDefault(e => e.EventId == g.EventId)?.Name
                            ?? $"#{g.EventId}";
            labels.Add($"{eventName}→{g.Target.Name}");
        }
        return labels;
    }

    /// <summary>
    /// Called when the user clicks chip at <paramref name="index"/>.
    /// Sets <see cref="HsmGlobalTransitionSelection"/> as the active sub-selection.
    /// </summary>
    public void OnChipClicked(int index)
    {
        if (index < 0 || index >= _asset.AllGlobalTransitions.Count) return;
        var g = _asset.AllGlobalTransitions[index];
        _store.ActiveSubSelection = new HsmGlobalTransitionSelection(g.VisualId);
    }

    /// <summary>
    /// Called when the user chooses "Remove" for chip at <paramref name="index"/>.
    /// Dispatches removal through <see cref="IHsmGlobalsCommandDispatcher"/>.
    /// </summary>
    public void OnChipRemoved(int index)
    {
        if (index < 0 || index >= _asset.AllGlobalTransitions.Count) return;
        var g = _asset.AllGlobalTransitions[index];
        _dispatcher.RemoveGlobalTransition(g.VisualId);
    }
}

// ── HsmGlobalsStrip ───────────────────────────────────────────────────────────

/// <summary>
/// Strip that renders a chip per global transition defined in the loaded HSM asset.
/// Click → sets <see cref="HsmGlobalTransitionSelection"/> sub-selection.
/// Context menu: edit / remove → dispatches through the command dispatcher.
/// ImGui calls are all guarded so headless tests can instantiate this class safely.
/// </summary>
public sealed class HsmGlobalsStrip
{
    private readonly HsmGlobalsStripLogic _logic;

    public HsmGlobalsStrip(HsmAsset asset, EditorSelectionStore store, IHsmGlobalsCommandDispatcher dispatcher)
    {
        _logic = new HsmGlobalsStripLogic(asset, store, dispatcher);
    }

    /// <summary>
    /// Exposes the logic layer for tests.
    /// </summary>
    internal HsmGlobalsStripLogic Logic => _logic;

    public void Render()
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return;

        var labels = _logic.GetChipLabels();
        if (labels.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("(no global transitions)");
            return;
        }

        for (int i = 0; i < labels.Count; i++)
        {
            if (i > 0) ImGuiNET.ImGui.SameLine();

            // Chip button.
            if (ImGuiNET.ImGui.Button(labels[i] + $"##gt{i}"))
                _logic.OnChipClicked(i);

            // Context menu: Edit / Change target / Remove.
            if (ImGuiNET.ImGui.BeginPopupContextItem($"##gt_ctx{i}"))
            {
                if (ImGuiNET.ImGui.MenuItem("Edit"))
                    _logic.OnChipClicked(i);  // also select it for inspector edit

                if (ImGuiNET.ImGui.MenuItem("Remove"))
                    _logic.OnChipRemoved(i);

                ImGuiNET.ImGui.EndPopup();
            }
        }
    }
}

// ── DefaultHsmGlobalsCommandDispatcher ───────────────────────────────────────

/// <summary>
/// Production implementation: removes the global transition directly from the asset
/// (marks dirty). A command-sink-backed version can be wired in later when
/// <see cref="HsmCommandSink"/> gains a RemoveGlobalTransition command.
/// </summary>
public sealed class DefaultHsmGlobalsCommandDispatcher : IHsmGlobalsCommandDispatcher
{
    private readonly HsmAsset _asset;

    public DefaultHsmGlobalsCommandDispatcher(HsmAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    public void RemoveGlobalTransition(Guid visualId)
    {
        if (_asset.RemoveGlobalTransition(visualId))
            _asset.MarkDirty();
    }
}
