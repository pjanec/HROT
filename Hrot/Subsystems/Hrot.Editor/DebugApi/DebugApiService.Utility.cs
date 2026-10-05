using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Utility;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ <c>CE-3069</c> G2 — <c>GET /entities/{networkId}/utility</c>: what an observed unit's utility decisions chose, why,
    /// and how often they changed. 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §5.1.
    /// <para>⭐ Arming is the EXISTING <c>POST /trace/observe {networkId, on:true}</c> — <c>TraceBufferLifecycleSystem</c>
    /// attaches the unit's <see cref="UtilityDecisionLog"/> and utility trace with the BTree/HSM ring; one switch for
    /// "observe this unit's AI". Read it on the BRAIN perspective (<c>Scenario</c> on a cluster) — the scorer runs there.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        private static Dictionary<ushort, string>? _utilityInputNames;

        /// <summary><c>GET /entities/{networkId}/utility</c>.</summary>
        public JsonNode GetEntityUtility(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity) || !_world.IsAlive(entity))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };

            if (!_world.IsComponentTypeRegistered<UtilityDecisionLog>() || !_world.HasComponent<UtilityDecisionLog>(entity))
                return new JsonObject
                {
                    ["networkId"] = networkId,
                    ["observed"]  = false,
                    ["note"]      = "Not observed. POST /trace/observe {networkId, on:true} on the Brain perspective (Scenario), " +
                                    "let the simulation tick, then read again.",
                };

            ref readonly var log = ref _world.GetComponentRO<UtilityDecisionLog>(entity);
            var decisions = new JsonArray();
            foreach (var slot in log.SlotsRO())
            {
                if (slot.DecisionId == 0) continue;
                UtilityDecisionCatalog.Shared.TryGet(slot.DecisionId, out var def, out float hysteresis);
                bool isOption = def is null || def.Kind == DecisionKind.PostureSelect;

                var ranked = new JsonArray();
                var entries = slot.RankedRO();
                for (int i = 0; i < slot.Count; i++)
                {
                    var e = entries[i];
                    var row = new JsonObject { ["score"] = Round(e.Score) };
                    if (isOption) row["option"] = OptionLabel(def, e.WinningPostureId);
                    else          AddCandidate(row, e.CandidateHandle);
                    ranked.Add(row);
                }

                var d = new JsonObject
                {
                    ["decisionId"]  = slot.DecisionId,
                    ["decision"]    = def?.DebugName ?? "(unregistered)",
                    ["kind"]        = def?.Kind.ToString() ?? "unknown",
                    ["hysteresis"]  = Round(hysteresis),
                    ["evalCount"]   = slot.EvalCount,
                    ["switchCount"] = slot.SwitchCount,
                    ["margin"]      = Round(slot.Margin),
                    ["ranked"]      = ranked,
                };
                if (isOption)
                {
                    d["winner"]         = slot.Winner == 0 ? null : OptionLabel(def, (int)slot.Winner);
                    d["previousWinner"] = slot.PreviousWinner == 0 ? null : OptionLabel(def, (int)slot.PreviousWinner);
                }
                else
                {
                    d["winner"]         = CandidateNode(slot.Winner);
                    d["previousWinner"] = CandidateNode(slot.PreviousWinner);
                }
                decisions.Add(d);
            }

            return new JsonObject
            {
                ["networkId"] = networkId,
                ["observed"]  = true,
                ["decisions"] = decisions,
                ["lastPass"]  = LastTracedPass(entity),
            };
        }

        /// <summary>
        /// The most recent fully traced scoring pass (an option decision: per option, every consideration's input, raw value,
        /// curve output and weight). Candidate rankings trace only their winner, so they do not appear here.
        /// </summary>
        private JsonNode? LastTracedPass(Entity entity)
        {
            if (!_world.HasComponent<UtilityTraceWorkingMemory1024>(entity)) return null;
            ref readonly var ring = ref _world.GetComponentRO<UtilityTraceWorkingMemory1024>(entity);
            var copy = ring;   // ReadRecord is a non-readonly member; read a copy
            int count = copy.RecordCount;
            // ⭐ The newest Winner record that has considerations before it: a candidate ranking (the threat ranking runs
            //   every tick too) writes a bare Winner record, which would otherwise hide the option decision's pass.
            int winnerIdx = -1;
            for (int i = count - 1; i >= 1 && winnerIdx < 0; i--)
            {
                copy.ReadRecord(i, out var r);
                if (r.OpCode != UtilityTraceOpCode.Winner) continue;
                copy.ReadRecord(i - 1, out var before);
                if (before.OpCode == UtilityTraceOpCode.Consideration) winnerIdx = i;
            }
            if (winnerIdx < 0) return null;

            copy.ReadRecord(winnerIdx, out var winner);
            var byOption = new SortedDictionary<int, JsonArray>();
            for (int i = winnerIdx - 1; i >= 0; i--)
            {
                copy.ReadRecord(i, out var r);
                if (r.OpCode == UtilityTraceOpCode.Winner) break;   // the previous pass
                if (!byOption.TryGetValue(r.OptionIndex, out var rows)) byOption[r.OptionIndex] = rows = new JsonArray();
                rows.Insert(0, new JsonObject
                {
                    ["input"]  = InputName(r.InputId),
                    ["raw"]    = Round(r.RawValue),
                    ["curved"] = Round(r.CurveOutput),
                    ["weight"] = Round(r.Weight),
                });
            }

            var options = new JsonArray();
            foreach (var (optionIndex, rows) in byOption)
                options.Add(new JsonObject { ["optionIndex"] = optionIndex, ["considerations"] = rows });

            return new JsonObject
            {
                ["winnerOptionId"] = winner.OptionIndex,
                ["winnerScore"]    = Round(winner.RawValue),
                ["margin"]         = Round(winner.RunningAggregate),
                ["options"]        = options,
            };
        }

        private static string OptionLabel(UtilityDecisionDef? def, int optionId)
            => def?.OptionName(optionId) ?? optionId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private void AddCandidate(JsonObject row, long handle)
        {
            var c = CandidateNode(handle);
            if (c is JsonObject o) foreach (var kv in o) row[kv.Key] = kv.Value?.DeepClone();
        }

        /// <summary>A candidate entity as {networkId, name}; null for none.</summary>
        private JsonNode? CandidateNode(long handle)
        {
            if (handle == 0) return null;
            var e = new Entity((ulong)handle);
            var node = new JsonObject();
            if (_world.IsAlive(e))
            {
                if (_entityMap.TryGetNetworkId(e, out long netId)) node["networkId"] = netId;
                if (_world.HasComponent<EntityInfo>(e)) node["name"] = _world.GetComponentRO<EntityInfo>(e).Name.ToString();
            }
            else node["gone"] = true;
            return node;
        }

        /// <summary>The input reader's name from the generated id tables (<c>StandardInputIds</c>, <c>SquadInputIds</c>).</summary>
        private static string InputName(ushort inputId)
        {
            if (_utilityInputNames is null)
            {
                var names = new Dictionary<ushort, string>();
                foreach (var t in new[] { typeof(StandardInputIds), typeof(SquadInputIds) })
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                        if (f.IsLiteral && f.FieldType == typeof(ushort))
                            names.TryAdd((ushort)f.GetRawConstantValue()!, f.Name);
                _utilityInputNames = names;
            }
            return _utilityInputNames.TryGetValue(inputId, out var n) ? n : $"0x{inputId:X4}";
        }

        private static double Round(float v) => Math.Round(v, 4);
    }
}
