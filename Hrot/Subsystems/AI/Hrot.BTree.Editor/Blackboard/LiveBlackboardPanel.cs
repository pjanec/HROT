using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using ImGuiNET;
using Hrot.BTree.Editor.Debug;

namespace Hrot.BTree.Editor.Blackboard;

/// <summary>
/// Helper that renders a read-only blackboard panel inside an existing ImGui window.
/// In Slice 2 (read-only mode) field values are live-read from the ECS blackboard component.
/// Call Draw() inside an already-opened ImGui window.
/// </summary>
public sealed class LiveBlackboardPanel
{
    private BlackboardSchema? _schema;
    private IBTreeDebugSession? _session;
    private EntityRepository? _repo;
    private Entity _entity;

    /// <summary>Sets the schema to display. Pass null to show an empty state.</summary>
    public void SetSchema(BlackboardSchema? schema) => _schema = schema;

    /// <summary>Sets the debug session used for live value reads (may be null).</summary>
    public void SetSession(IBTreeDebugSession? session) => _session = session;

    /// <summary>
    /// Sets the ECS context used for reading live blackboard values.
    /// Pass null to clear the context (values show as "--").
    /// </summary>
    public void SetEntityContext(EntityRepository? repo, Entity entity)
    {
        _repo   = repo;
        _entity = entity;
    }

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
                if (sessionActive && _repo != null && _repo.HasComponent<BrainBlackboard>(_entity))
                {
                    ref readonly var bb = ref _repo.GetComponentRO<BrainBlackboard>(_entity);
                    string display = field.FieldOffset >= 0
                                     && field.FieldOffset < BehaviorConstants.MaxBehaviorParamByteSize
                        ? ReadFieldValue(in bb, field)
                        : "--";
                    ImGui.TextUnformatted(display);
                }
                else if (sessionActive)
                {
                    ImGui.TextDisabled("--");
                }
                else
                {
                    ImGui.TextDisabled("offline");
                }
            }

            ImGui.EndTable();
        }
    }

    private static unsafe string ReadFieldValue(in BrainBlackboard bb, BlackboardField field)
    {
        ref var bbMut = ref Unsafe.AsRef(in bb);
        BrainBlackboard* bbPtr = (BrainBlackboard*)Unsafe.AsPointer(ref bbMut);
        byte* basePtr = bbPtr->BehaviorParameters;
        byte* fieldPtr = basePtr + field.FieldOffset;

        Type t = field.FieldType;
        if (t == typeof(bool))    return (*(bool*)fieldPtr).ToString();
        if (t == typeof(byte))    return (*fieldPtr).ToString();
        if (t == typeof(sbyte))   return (*(sbyte*)fieldPtr).ToString();
        if (t == typeof(short))   return (*(short*)fieldPtr).ToString();
        if (t == typeof(ushort))  return (*(ushort*)fieldPtr).ToString();
        if (t == typeof(int))     return (*(int*)fieldPtr).ToString();
        if (t == typeof(uint))    return (*(uint*)fieldPtr).ToString();
        if (t == typeof(long))    return (*(long*)fieldPtr).ToString();
        if (t == typeof(ulong))   return (*(ulong*)fieldPtr).ToString();
        if (t == typeof(float))   return (*(float*)fieldPtr).ToString("G6");
        if (t == typeof(double))  return (*(double*)fieldPtr).ToString("G10");
        if (t == typeof(Vector2)) return (*(Vector2*)fieldPtr).ToString();
        if (t == typeof(Vector3)) return (*(Vector3*)fieldPtr).ToString();
        if (t == typeof(Vector4)) return (*(Vector4*)fieldPtr).ToString();
        return "?";
    }
}
