using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fhsm.Kernel.Data;
using ImGuiNET;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// Entity-aware ImGui renderer for <see cref="HsmTraceWorkingMemory1024"/>.
/// Iterates the per-entity ring buffer in chronological order and decodes each
/// 16-byte record into a readable table row, using the active behavior's
/// <see cref="BehaviorDefinition.HsmMetadata"/> to symbolicate state, event,
/// and action IDs.
/// </summary>
[ImGuiRenderer(typeof(HsmTraceWorkingMemory1024))]
public sealed class HsmTraceWorkingMemoryRenderer : IEntityAwareImGuiRenderer
{
    public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }

    public string? GetSummary(object value)
    {
        if (value is HsmTraceWorkingMemory1024 m)
            return $"HSM Trace ({m.RecordCount} records)";
        return "HSM Execution Trace";
    }

    public bool RenderValue(object value) => false;

    public unsafe bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
    {
        doubleClickedPath = null;
        if (value is not HsmTraceWorkingMemory1024 traceData) return false;

        ImGui.TextDisabled($"Records: {traceData.RecordCount} / {HsmTraceWorkingMemory1024.CapacityRecords}    Cursor: {traceData.WritePos} bytes    InstanceId: {traceData.LastInstanceId}");
        ImGui.Separator();

        if (traceData.RecordCount == 0)
        {
            ImGui.TextDisabled("No trace history.");
            return true;
        }

        MachineMetadata? meta = null;
        if (BehaviorRegistryAccessor != null && session.HasComponent(entity, typeof(BehaviorState)))
        {
            var boxed = session.GetComponent(entity, typeof(BehaviorState));
            if (boxed is BehaviorState bs &&
                BehaviorRegistryAccessor.TryGetDefinition(bs.ActiveBehaviorHash, out var def))
            {
                meta = def.HsmMetadata;
            }
        }

        int startOffset = traceData.RecordCount == HsmTraceWorkingMemory1024.CapacityRecords
            ? traceData.WritePos
            : 0;

        if (ImGui.BeginTable("HsmTraceTable", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Tick", ImGuiTableColumnFlags.WidthFixed, 50f);
            ImGui.TableSetupColumn("OpCode", ImGuiTableColumnFlags.WidthFixed, 130f);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableHeadersRow();

            int payloadBytes = HsmTraceWorkingMemory1024.PayloadBytes;
            int stride       = HsmTraceWorkingMemory1024.RecordStride;

            byte* bufferPtr = (byte*)Unsafe.AsPointer(ref traceData.Buffer[0]);
            for (int i = 0; i < traceData.RecordCount; i++)
            {
                int offset = (startOffset + (i * stride)) % payloadBytes;
                TraceRecord* rec = (TraceRecord*)(bufferPtr + offset);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(rec->Timestamp.ToString());

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(rec->OpCode.ToString());

                ImGui.TableSetColumnIndex(2);
                RenderDetail(rec, meta);

                ImGui.TableSetColumnIndex(3);
                RenderResult(rec);
            }

            ImGui.EndTable();
        }

        return true;
    }

    private static unsafe void RenderDetail(TraceRecord* rec, MachineMetadata? meta)
    {
        switch (rec->OpCode)
        {
            case TraceOpCode.StateEnter:
            case TraceOpCode.StateExit:
                ImGui.TextUnformatted($"{rec->StateIndex} ({Name(meta, rec->StateIndex, NameKind.State)})");
                break;
            case TraceOpCode.Transition:
                ImGui.TextUnformatted(
                    $"{rec->StateIndex} ({Name(meta, rec->StateIndex, NameKind.State)}) -> " +
                    $"{rec->TargetStateIndex} ({Name(meta, rec->TargetStateIndex, NameKind.State)})  " +
                    $"[Event {rec->TriggerEventId} ({Name(meta, rec->TriggerEventId, NameKind.Event)})]");
                break;
            case TraceOpCode.EventHandled:
                ImGui.TextUnformatted($"event={rec->EventId} ({Name(meta, rec->EventId, NameKind.Event)})");
                break;
            case TraceOpCode.ActionExecuted:
                ImGui.TextUnformatted($"action={rec->ActionId} ({Name(meta, rec->ActionId, NameKind.Action)})");
                break;
            case TraceOpCode.GuardEvaluated:
                ImGui.TextUnformatted($"guard={rec->GuardId} ({Name(meta, rec->GuardId, NameKind.Action)})");
                break;
            case TraceOpCode.Error:
                ImGui.TextUnformatted($"err={rec->ErrorCode}");
                break;
            default:
                ImGui.TextDisabled("-");
                break;
        }
    }

    private static unsafe void RenderResult(TraceRecord* rec)
    {
        switch (rec->OpCode)
        {
            case TraceOpCode.GuardEvaluated:
                ImGui.TextUnformatted(rec->GuardResult != 0 ? "PASS" : "FAIL");
                break;
            case TraceOpCode.Transition:
                ImGui.TextUnformatted("OK");
                break;
            default:
                ImGui.TextDisabled("-");
                break;
        }
    }

    private enum NameKind { State, Event, Action }

    private static string Name(MachineMetadata? meta, ushort id, NameKind kind) => kind switch
    {
        NameKind.State  => meta?.GetStateName(id) ?? "?",
        NameKind.Event  => meta?.GetEventName(id) ?? "?",
        NameKind.Action => meta?.GetActionName(id) ?? "?",
        _ => "?",
    };
}
