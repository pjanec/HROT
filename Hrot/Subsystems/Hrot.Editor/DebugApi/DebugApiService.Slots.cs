using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Behavior;

namespace Hrot.Editor.DebugApi
{
    public sealed partial class DebugApiService
    {
        /// <summary>
        /// ⭐⭐ <c>CE-2288</c> (CC-16, R-2050) — <c>GET /entities/{networkId}/slots</c>: EVERY occurrence slot the unit carries — the running
        /// task's, the SOP's, a paused task's, their hosted children's and working-state slots — named by its owner and role, its value
        /// decoded through the layout the root dump already uses (<see cref="OccurrenceSlotCatalog"/>). An unnamed slot is still listed.
        /// </summary>
        public JsonNode GetEntitySlots(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };
            var registry = _behaviorRegistry;
            if (registry == null)
                return new JsonObject { ["networkId"] = networkId, ["error"] = "This node has no behaviour registry (not a brain host)." };
            var slots = new JsonArray();
            // ⭐ CE-1320 — the blueprint registry this service holds is PASSED (attached blueprints are named and decoded)
            foreach (var s in OccurrenceSlotCatalog.Read(_world, unit, registry, _blueprintRegistry))
            {
                slots.Add(new JsonObject
                {
                    ["key"] = s.Key, ["kind"] = s.Kind.ToString(), ["payloadSize"] = s.PayloadSize, ["structureHash"] = s.StructureHash,
                    ["owner"] = s.Owner.ToString(), ["behaviour"] = s.Behaviour, ["role"] = s.Role.ToString(), ["label"] = s.Label,
                    ["layout"] = s.LayoutType,
                    ["value"] = s.Value != null ? JsonSerializer.SerializeToNode(s.Value, FdpJsonOptionsRegistry.DefaultRelaxed) : null,
                });
            }
            return new JsonObject { ["networkId"] = networkId, ["count"] = slots.Count, ["slots"] = slots };
        }
    
        /// <summary>
        /// ⭐⭐ <c>CE-1320</c> (Brain View §12g BV-65/66) — <c>POST /entities/{id}/variable</c> with <c>owner</c>: STAGE a write to one field of
        /// any behaviour slot (the task's, the SOP's, a paused task's, an attached blueprint's; a node's working state or a hosted child via
        /// <c>part</c>), params or variables, through <see cref="Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter"/> — the ONE live write
        /// the Brain tab uses too. ⛔ Never restarts the behaviour. <c>paramsJson</c> instead of <c>path</c>/<c>value</c> applies a whole
        /// parameter set through the behaviour's own parser.
        /// </summary>
        public (JsonNode? result, string? error, string? hintCategory) StageSlotField(
            long networkId, string owner, string? behaviour, string? part, string? role, string? path, JsonNode? value, string? paramsJson)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return (null, $"Entity {networkId} not found.", DebugApiHints.Entity);
            if (_behaviorRegistry is not { } registry)
                return (null, "This node has no behaviour registry (not a brain host).", DebugApiHints.Variable);
            if (_bpManager is null)
                return (null, "This editor has no staged-write target, so the edit has nowhere to go.", DebugApiHints.Variable);
            SlotOwnerKind ownerKind = owner.ToLowerInvariant() switch
            {
                "task" => SlotOwnerKind.Task, "sop" => SlotOwnerKind.Sop, "paused" or "pausedtask" => SlotOwnerKind.PausedTask,
                "blueprint" => SlotOwnerKind.InstanceBlueprint, _ => SlotOwnerKind.OwnerUnknown,
            };
            if (ownerKind == SlotOwnerKind.OwnerUnknown)
                return (null, $"owner '{owner}' is not one of task · sop · paused · blueprint.", DebugApiHints.Variable);
            var halfRole = (role ?? "variables").ToLowerInvariant() switch
            {
                "params" or "parameters" => OccurrenceSlotCatalog.SlotFieldRole.FieldParams,
                _ => OccurrenceSlotCatalog.SlotFieldRole.FieldVariables,
            };
            var stage = new Hrot.Editor.AiShared.Variables.FieldStager(_bpManager.StageFieldMutation);

            Hrot.Editor.AiShared.Variables.SlotWriteResult r;
            if (paramsJson is not null)
                r = Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter.WriteParamsJson(_world, unit, registry, _blueprintRegistry, stage,
                    ownerKind, behaviour, paramsJson);
            else if (string.IsNullOrWhiteSpace(path) || value is null)
                return (null, "path and value are required (or paramsJson for a whole parameter set).", DebugApiHints.Variable);
            else
                r = Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter.WriteField(_world, unit, registry, _blueprintRegistry, stage,
                    ownerKind, behaviour, part, halfRole, path!, value);
            if (r.Error is not null) return (null, r.Error, DebugApiHints.Variable);

            bool stopped = _bpManager.IsPaused;
            return (new JsonObject
            {
                ["networkId"] = networkId, ["owner"] = r.Address!.Owner.ToString(), ["behaviour"] = r.Address.Behaviour,
                ["part"] = r.Address.Part, ["role"] = paramsJson is not null ? "params" : (halfRole == OccurrenceSlotCatalog.SlotFieldRole.FieldParams ? "params" : "variables"),
                ["path"] = paramsJson is not null ? null : path, ["component"] = r.Address.ComponentType.Name, ["offset"] = r.Address.ComponentOffset,
                ["size"] = r.Address.Size, ["staged"] = true, ["pending"] = true, ["willDrain"] = !stopped,
                ["startRecordUpdated"] = r.StartRecordUpdated, ["restarted"] = false,
                ["note"] = stopped
                    ? "Staged, but the debugger is stopped at a breakpoint and the drain is gated on resuming — POST /breakpoints/continue."
                    : "Staged. It lands on the next advancing tick, in place — the behaviour keeps running (no restart).",
            }, null, null);
        }
}
}
