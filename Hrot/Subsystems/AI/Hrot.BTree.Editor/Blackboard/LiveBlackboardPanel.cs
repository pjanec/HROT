using ImGuiNET;
using Hrot.BTree.Editor.Debug;

namespace Hrot.BTree.Editor.Blackboard;

/// <summary>
/// Helper that renders a read-only blackboard schema panel inside an existing ImGui window.
/// In Slice 2 (read-only mode) field values are not yet wired to the kernel and show as "--".
/// Call Draw() inside an already-opened ImGui window.
/// </summary>
public sealed class LiveBlackboardPanel
{
    private BlackboardSchema? _schema;
    private IBTreeDebugSession? _session;

    /// <summary>Sets the schema to display. Pass null to show an empty state.</summary>
    public void SetSchema(BlackboardSchema? schema) => _schema = schema;

    /// <summary>Sets the debug session used for live value reads (may be null).</summary>
    public void SetSession(IBTreeDebugSession? session) => _session = session;

    /// <summary>
    /// Draws the blackboard panel content. Must be called inside an open ImGui window.
    /// </summary>
    public void Draw()
    {
        if (_schema is null)
        {
            ImGui.TextDisabled("No blackboard schema loaded.");
            return;
        }

        bool sessionActive = _session?.IsAttached == true
                             && _session.GetCurrentStateSnapshot() is not null;

        ImGui.Separator();
        ImGui.TextDisabled($"Blackboard: {_schema.StructType.Name}");
        ImGui.Separator();

        if (ImGui.BeginTable("bb_fields", 3,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Field",  ImGuiTableColumnFlags.WidthStretch, 0.40f);
            ImGui.TableSetupColumn("Type",   ImGuiTableColumnFlags.WidthStretch, 0.30f);
            ImGui.TableSetupColumn("Value",  ImGuiTableColumnFlags.WidthStretch, 0.30f);
            ImGui.TableHeadersRow();

            foreach (var field in _schema.Fields)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(field.Name);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextDisabled(field.FieldType.Name);

                ImGui.TableSetColumnIndex(2);
                // Live values not yet wired (Slice 3+); show placeholder.
                if (sessionActive)
                    ImGui.TextDisabled("--");
                else
                    ImGui.TextDisabled("offline");
            }

            ImGui.EndTable();
        }
    }
}
