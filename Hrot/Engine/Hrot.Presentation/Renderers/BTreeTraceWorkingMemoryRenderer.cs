using System.Runtime.CompilerServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using ImGuiNET;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// Entity-aware ImGui renderer for <see cref="BTreeTraceWorkingMemory1024"/>.
/// Iterates the per-entity ring buffer in chronological order and decodes each
/// 16-byte record into a readable table row. Node-index → label resolution uses
/// the active behavior's <see cref="Fbt.BehaviorTreeBlob.DebugMetadata"/>.
/// </summary>
[ImGuiRenderer(typeof(BTreeTraceWorkingMemory1024))]
public sealed class BTreeTraceWorkingMemoryRenderer : IEntityAwareImGuiRenderer
{
    /// <summary>
    /// Set once at startup (composition root). Required for symbolicating
    /// node indices against the active behavior's tree blob.
    /// </summary>
    public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }

    public string? GetSummary(object value)
    {
        if (value is BTreeTraceWorkingMemory1024 m)
            return $"BTree Trace ({m.RecordCount} records)";
        return "BTree Execution Trace";
    }

    public bool RenderValue(object value) => false;

    public unsafe bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
    {
        doubleClickedPath = null;
        if (value is not BTreeTraceWorkingMemory1024 traceData) return false;

        ImGui.TextDisabled($"Records: {traceData.RecordCount} / {BTreeTraceWorkingMemory1024.CapacityRecords}    Cursor: {traceData.WritePos} bytes    InstanceId: {traceData.LastInstanceId}");
        ImGui.Separator();

        if (traceData.RecordCount == 0)
        {
            ImGui.TextDisabled("No trace history.");
            return true;
        }

        // Resolve the BehaviorTreeBlob via the entity's active behavior hash.
        Fbt.BehaviorTreeBlob? blob = null;
        if (BehaviorRegistryAccessor != null && session.HasComponent(entity, typeof(BehaviorState)))
        {
            var boxed = session.GetComponent(entity, typeof(BehaviorState));
            if (boxed is BehaviorState bs &&
                BehaviorRegistryAccessor.TryGetDefinition(bs.ActiveBehaviorHash, out var def))
            {
                blob = def.BTreeInterpreter?.Blob;
            }
        }

        // Ring iteration: when buffer is full, oldest record sits at offset == WritePos.
        // When not full, oldest record sits at offset 0. WritePos is already pre-wrapped in [0, PayloadBytes).
        int startOffset = traceData.RecordCount == BTreeTraceWorkingMemory1024.CapacityRecords
            ? traceData.WritePos
            : 0;

        if (ImGui.BeginTable("BTreeTraceTable", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Tick", ImGuiTableColumnFlags.WidthFixed, 50f);
            ImGui.TableSetupColumn("OpCode", ImGuiTableColumnFlags.WidthFixed, 110f);
            ImGui.TableSetupColumn("Node / Detail", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, 80f);
            ImGui.TableHeadersRow();

            int payloadBytes = BTreeTraceWorkingMemory1024.PayloadBytes;
            int stride       = BTreeTraceWorkingMemory1024.RecordStride;

            // `traceData` is a stack-local boxed-unboxed value; the `fixed byte Buffer[]`
            // field is already inline in this stack copy. Take its address via Unsafe.AsPointer.
            byte* bufferPtr = (byte*)Unsafe.AsPointer(ref traceData.Buffer[0]);
            for (int i = 0; i < traceData.RecordCount; i++)
            {
                int offset = (startOffset + (i * stride)) % payloadBytes;
                BTreeTraceRecord* rec = (BTreeTraceRecord*)(bufferPtr + offset);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(rec->Timestamp.ToString());

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(rec->OpCode.ToString());

                ImGui.TableSetColumnIndex(2);
                RenderDetail(rec, blob);

                ImGui.TableSetColumnIndex(3);
                RenderResult(rec);
            }

            ImGui.EndTable();
        }

        return true;
    }

    private static unsafe void RenderDetail(BTreeTraceRecord* rec, Fbt.BehaviorTreeBlob? blob)
    {
        switch (rec->OpCode)
        {
            case BTreeTraceOpCode.NodeEvaluated:
            case BTreeTraceOpCode.WaitStarted:
            case BTreeTraceOpCode.WaitCompleted:
            case BTreeTraceOpCode.ChannelMutated:
            case BTreeTraceOpCode.Error:
                ImGui.TextUnformatted($"{rec->NodeIndex} ({ResolveNodeLabel(blob, rec->NodeIndex)})");
                break;
            case BTreeTraceOpCode.ScopePushed:
            case BTreeTraceOpCode.ScopePopped:
                ImGui.TextUnformatted($"depth={rec->StackDepth}");
                break;
            default:
                ImGui.TextDisabled("-");
                break;
        }
    }

    private static unsafe void RenderResult(BTreeTraceRecord* rec)
    {
        switch (rec->OpCode)
        {
            case BTreeTraceOpCode.NodeEvaluated:
                ImGui.TextUnformatted(rec->Status.ToString());
                break;
            case BTreeTraceOpCode.WaitStarted:
            case BTreeTraceOpCode.WaitCompleted:
                ImGui.TextUnformatted($"{rec->Duration:F2}s");
                break;
            case BTreeTraceOpCode.ChannelMutated:
                // ChannelKind enum stored as byte; ActiveAction is numeric only.
                ImGui.TextUnformatted($"{(ChannelKind)rec->Channel}/{rec->ActiveAction}/{rec->ChannelStatus}");
                break;
            case BTreeTraceOpCode.Error:
                ImGui.TextUnformatted($"err={rec->ErrorCode}");
                break;
            default:
                ImGui.TextDisabled("-");
                break;
        }
    }

    private static string ResolveNodeLabel(Fbt.BehaviorTreeBlob? blob, ushort nodeIndex)
    {
        if (blob?.DebugMetadata == null) return "?";
        if (nodeIndex >= blob.DebugMetadata.Length) return "?";
        var label = blob.DebugMetadata[nodeIndex].Label;
        return string.IsNullOrEmpty(label) ? "?" : label;
    }
}
