using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using ImGuiNET;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 3) — this window's own state, dumped.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⛔ <see cref="CallstackWindow"/> has no id of its own — <c>BlueprintEditorWindowBase</c>
/// declares <c>Title</c> and nothing else *(📄 <c>PanelIds.cs</c>'s own remarks on the one family with
/// no window id)*. ⭐ It IS a singleton, registered once by <c>BlueprintWindowRegistrar</c> under the
/// name <c>"Callstack"</c> ⇒ a declared literal serves as both the address and the kind.</para>
/// </summary>
public sealed record CallstackWindowPanelViewModel(
    string PanelId,
    string PanelKind,
    IReadOnlyList<CallFrame> Frames) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump()
    {
        var frames = new JsonArray();
        foreach (var f in Frames)
            frames.Add(new JsonObject
            {
                ["depth"]         = f.Depth,
                ["peerAssetId"]   = f.PeerAssetIdString,
                ["methodName"]    = f.MethodName,
            });

        return new JsonObject
        {
            ["panelId"]   = PanelId,
            ["panelKind"] = PanelKind,
            ["frames"]    = frames,
        };
    }
}

public sealed class CallstackWindow : BlueprintEditorWindowBase
{
    /// <summary>⭐ <c>U-obs-5</c> — THE ADDRESS/KIND. ⛔ A declared literal — see the view-model's
    /// class remarks for why a singleton with no <c>Id</c> may use one string for both roles.</summary>
    internal const string PanelId = "callstack";

    private readonly IBlueprintDebugSession _session;
    private readonly EditorSelectionStore _selectionStore;

    public override string Title => "Callstack";

    // Captured on each DrawUI call -- readable by tests without an ImGui context.
    public IReadOnlyList<CallFrame>? LastRenderedFrames { get; private set; }

    public CallstackWindow(IBlueprintDebugSession session, EditorSelectionStore selectionStore)
    {
        _session        = session        ?? throw new ArgumentNullException(nameof(session));
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(PanelId);
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. ⛔⛔ No ImGui — the frames were already captured before
    /// the render guard by the pre-existing <c>LastRenderedFrames</c> convention; this just publishes
    /// the same read.</summary>
    private CallstackWindowPanelViewModel BuildAndPublish(IReadOnlyList<CallFrame> frames)
    {
        var vm = new CallstackWindowPanelViewModel(PanelId, PanelId, frames);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal CallstackWindowPanelViewModel SimulateDrawUI()
        => BuildAndPublish(_session.GetCurrentCallStack());

    public override void DrawUI()
    {
        // Use GetCurrentCallStack() (peer-call frame stack, Editor DD §8.7).
        var frames = _session.GetCurrentCallStack();

        LastRenderedFrames = frames;
        BuildAndPublish(frames);

        // ImGui rendering requires a live context; skip in headless / test environments.
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;

        if (frames.Count == 0)
        {
            ImGui.TextDisabled("No call stack.");
            return;
        }

        if (ImGui.BeginTable("##callstack", 3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Depth");
            ImGui.TableSetupColumn("Asset");
            ImGui.TableSetupColumn("Method");
            ImGui.TableHeadersRow();

            foreach (var frame in frames)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(frame.Depth.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(frame.PeerAssetIdString);
                ImGui.TableNextColumn();
                ImGui.Text(frame.MethodName);
            }

            ImGui.EndTable();
        }
    }

    public override void OnActivated()   { }
    public override void OnDeactivated() { }
}
