using ImGuiNET;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

public sealed class CallstackWindow : BlueprintEditorWindowBase
{
    private readonly IBlueprintDebugSession _session;
    private readonly EditorSelectionStore _selectionStore;

    public override string Title => "Callstack";

    // Captured on each DrawUI call -- readable by tests without an ImGui context.
    public IReadOnlyList<CallFrame>? LastRenderedFrames { get; private set; }

    public CallstackWindow(IBlueprintDebugSession session, EditorSelectionStore selectionStore)
    {
        _session        = session        ?? throw new ArgumentNullException(nameof(session));
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
    }

    public override void DrawUI()
    {
        // Use GetCurrentCallStack() (peer-call frame stack, Editor DD §8.7).
        var frames = _session.GetCurrentCallStack();

        LastRenderedFrames = frames;

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
