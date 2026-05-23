using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Scenario;
using Fhsm.Kernel.Data;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Scenario translator that projects the per-entity <see cref="HsmTraceWorkingMemory1024"/>
    /// ring buffer into a JSON array for diagnostic dumps. State/event/action IDs are
    /// resolved against the active behavior's <see cref="BehaviorDefinition.HsmMetadata"/>.
    /// </summary>
    public sealed class HsmTraceWorkingMemoryTranslator : IEntityScenarioTranslator
    {
        private const string Key = nameof(HsmTraceWorkingMemory1024);

        private readonly BehaviorRegistry _registry;

        public HsmTraceWorkingMemoryTranslator(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public bool IsExtractionSafe => true;

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(HsmTraceWorkingMemory1024));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<HsmTraceWorkingMemory1024>(entity);

        public unsafe Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            ref readonly var traceData = ref repo.GetComponentRO<HsmTraceWorkingMemory1024>(entity);

            MachineMetadata? meta = null;
            if (repo.HasComponent<BehaviorState>(entity))
            {
                var state = repo.GetComponentRO<BehaviorState>(entity);
                if (_registry.TryGetDefinition(state.ActiveBehaviorHash, out var def))
                    meta = def.HsmMetadata;
            }

            var recordsArray = new JsonArray();
            int payloadBytes = HsmTraceWorkingMemory1024.PayloadBytes;
            int stride       = HsmTraceWorkingMemory1024.RecordStride;

            int startOffset = traceData.RecordCount == HsmTraceWorkingMemory1024.CapacityRecords
                ? traceData.WritePos : 0;

            fixed (byte* bufferPtr = traceData.Buffer)
            {
                for (int i = 0; i < traceData.RecordCount; i++)
                {
                    int offset = (startOffset + (i * stride)) % payloadBytes;
                    TraceRecord* rec = (TraceRecord*)(bufferPtr + offset);

                    var recordObj = new JsonObject
                    {
                        ["Timestamp"]  = rec->Timestamp,
                        ["OpCode"]     = rec->OpCode.ToString(),
                        ["InstanceId"] = rec->InstanceId,
                    };

                    switch (rec->OpCode)
                    {
                        case TraceOpCode.StateEnter:
                        case TraceOpCode.StateExit:
                            recordObj["StateIndex"] = rec->StateIndex;
                            recordObj["StateName"]  = meta?.GetStateName(rec->StateIndex) ?? "?";
                            break;
                        case TraceOpCode.Transition:
                            recordObj["SourceStateIndex"]  = rec->StateIndex;
                            recordObj["SourceStateName"]   = meta?.GetStateName(rec->StateIndex) ?? "?";
                            recordObj["TargetStateIndex"]  = rec->TargetStateIndex;
                            recordObj["TargetStateName"]   = meta?.GetStateName(rec->TargetStateIndex) ?? "?";
                            recordObj["TriggerEventId"]    = rec->TriggerEventId;
                            recordObj["TriggerEventName"]  = meta?.GetEventName(rec->TriggerEventId) ?? "?";
                            break;
                        case TraceOpCode.EventHandled:
                            recordObj["EventId"]   = rec->EventId;
                            recordObj["EventName"] = meta?.GetEventName(rec->EventId) ?? "?";
                            break;
                        case TraceOpCode.ActionExecuted:
                            recordObj["ActionId"]   = rec->ActionId;
                            recordObj["ActionName"] = meta?.GetActionName(rec->ActionId) ?? "?";
                            break;
                        case TraceOpCode.GuardEvaluated:
                            recordObj["GuardId"]     = rec->GuardId;
                            recordObj["GuardName"]   = meta?.GetActionName(rec->GuardId) ?? "?";
                            recordObj["GuardResult"] = rec->GuardResult != 0;
                            break;
                        case TraceOpCode.Error:
                            recordObj["ErrorCode"] = rec->ErrorCode;
                            break;
                    }

                    recordsArray.Add(recordObj);
                }
            }

            var root = new JsonObject
            {
                ["RecordCount"] = traceData.RecordCount,
                ["WritePos"]    = traceData.WritePos,
                ["History"]     = recordsArray,
            };
            return new Dictionary<string, object> { [Key] = root };
        }

        public void Inject(EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver) { }

        public IEnumerable<string> GetOutputDomKeys() { yield return Key; }
    }
}

