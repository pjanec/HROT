using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad.Command;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐ <c>CE-2295</c> CC-E (CC-12…CC-15, PC-11, R-2045) — the reports up the chain of command on the debug HTTP: a commander's
    /// picture and a unit's outbox (<c>GET /entities/{id}/reports</c>), a fake report AS IF the unit's staff sent it
    /// (<c>POST /entities/{id}/reports</c>), lost comms (<c>POST /entities/{id}/reports/mute</c>), and the node's report log
    /// (<c>GET /squad/reports</c>). The INTERPRETED view — ages, names, overdue; the raw components are in <c>GET /entities/{id}</c>
    /// (CC-16). Read on the unit's BRAIN node. 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §7c.
    /// </summary>
    public sealed partial class DebugApiService
    {
        /// <summary>A subordinate silent this long (sim s) is overdue (CB-34: "Silent after 60 s").</summary>
        public const float ReportOverdueSeconds = 60f;

        /// <summary><c>GET /entities/{networkId}/reports</c>.</summary>
        public JsonNode GetEntityReports(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };
            float now = CommandPictureOps.Now(_world);
            var node = new JsonObject { ["networkId"] = networkId, ["now"] = now };
            if (_world.IsComponentTypeRegistered<BehaviorState>() && _world.HasComponent<BehaviorState>(unit))
                node["onBrainNode"] = _world.HasAuthority<BehaviorState>(unit);

            if (_world.IsComponentTypeRegistered<CommandPicture>() && _world.HasComponent<CommandPicture>(unit))
            {
                var pic = _world.GetComponentRO<CommandPicture>(unit);
                var elements = new JsonArray();
                for (int i = 0; i < pic.Elements.Count; i++)
                {
                    var e = pic.Elements.Items[i];
                    elements.Add(new JsonObject
                    {
                        ["from"] = e.FromNetId, ["size"] = e.Size.ToString(), ["count"] = e.Count, ["activity"] = e.Activity.ToString(),
                        ["equipment"] = e.Equipment.ToString(), ["at"] = V3(e.At), ["radius"] = e.Radius,
                        ["observedAt"] = e.ObservedAt, ["ageSeconds"] = now - e.ObservedAt, ["receivedAt"] = e.ReceivedAt,
                        ["seq"] = e.Seq, ["synthetic"] = e.Synthetic != 0,
                    });
                }
                var subs = new JsonArray();
                for (int i = 0; i < pic.Subordinates.Count; i++)
                {
                    var r = pic.Subordinates.Items[i];
                    float age = r.LastReportAt > 0f ? now - r.LastReportAt : float.PositiveInfinity;
                    var row = new JsonObject
                    {
                        ["networkId"] = r.UnitNetId, ["progress"] = r.Progress.ToString(), ["strength"] = r.Strength.ToString(),
                        ["ammo"] = r.Ammo.ToString(), ["phase"] = r.Phase, ["lastSeq"] = r.LastSeq,
                        ["lastReportAt"] = r.LastReportAt > 0f ? r.LastReportAt : null,
                        ["orderedAt"] = r.OrderedAt > 0f ? r.OrderedAt : null,
                        ["ageSeconds"] = float.IsInfinity(age) ? null : age, ["overdue"] = age > ReportOverdueSeconds,
                    };
                    if (_entityMap.TryGetEntity(r.UnitNetId, out var sub) && CommandReportText.UnitName(_world, sub) is { } subName)
                        row["name"] = subName;   // ⭐ CE-1191 — the shared name lookup (also the Reports window's)
                    subs.Add(row);
                }
                var reqs = new JsonArray();
                for (int i = 0; i < pic.Requests.Count; i++)
                {
                    var q = pic.Requests.Items[i];
                    reqs.Add(new JsonObject { ["from"] = q.FromNetId, ["kind"] = q.Kind.ToString(), ["at"] = V3(q.At), ["receivedAt"] = q.ReceivedAt, ["seq"] = q.Seq });
                }
                node["picture"] = new JsonObject { ["elements"] = elements, ["subordinates"] = subs, ["requests"] = reqs };
            }

            if (_world.IsComponentTypeRegistered<ReportingRequirements>() && _world.HasComponent<ReportingRequirements>(unit))
            {
                var req = _world.GetComponentRO<ReportingRequirements>(unit);
                node["requirements"] = new JsonObject
                {
                    ["pir"] = req.Pir.ToString(), ["ffir"] = req.Ffir.ToString(), ["sitrepEverySeconds"] = req.SitrepEverySeconds,
                    ["pirArea"] = req.PirArea, ["setBy"] = req.SetBy.ToString(),
                };
            }

            if (_world.IsComponentTypeRegistered<ReportOutbox>() && _world.HasComponent<ReportOutbox>(unit))
            {
                var ob = _world.GetComponentRO<ReportOutbox>(unit);
                var queued = new JsonArray();
                for (int i = 0; i < ob.Queued.Count; i++) queued.Add(ReportNode(ob.Queued.Items[i].Report, now, ob.Queued.Items[i].DueAt));
                node["outbox"] = new JsonObject
                {
                    ["queued"] = queued, ["nextSeq"] = ob.NextSeq, ["posture"] = ob.Posture.ToString(),
                    ["muted"] = ob.MutedUntil > now, ["mutedUntil"] = ob.MutedUntil > now ? ob.MutedUntil : null,
                };
            }
            if (node.Count <= 3) node["note"] = "No picture, requirements or outbox here — not a commander, or not its brain node (read on the Scenario perspective on a cluster).";
            return node;
        }

        /// <summary><c>POST /entities/{networkId}/reports</c> — a report AS IF the unit's staff sent it (<see cref="ReportInjection.Inject"/>):
        /// <c>{kind, subtype?, count?, size?, activity?, equipment?, at?:[x,y,z] | x?, y?, radius?, strength?, ammo?, phase?,
        /// observedAgoSeconds?}</c>. Enums by name (R-2007).</summary>
        public (JsonNode? result, int status, string? error) PostEntityReport(long networkId, JsonNode? body)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return (null, 404, $"Entity {networkId} not found.");
            if (!TryEnum<ReportKind>(body?["kind"], out var kind) || kind == ReportKind.NoReport)
                return (null, 400, $"kind is required: one of {string.Join(", ", Enum.GetNames<ReportKind>())}.");
            var r = new CommandReport { Kind = kind };
            var subtype = body?["subtype"];
            if (subtype != null)
            {
                if (kind == ReportKind.Progress && TryEnum<ProgressKind>(subtype, out var pk)) r.Subtype = (byte)pk;
                else if (kind == ReportKind.Request && TryEnum<RequestKind>(subtype, out var rk)) r.Subtype = (byte)rk;
                else return (null, 400, kind is ReportKind.Progress or ReportKind.Request
                    ? $"subtype must name a {(kind == ReportKind.Progress ? nameof(ProgressKind) : nameof(RequestKind))} value."
                    : "subtype is only for Progress and Request.");
            }
            if (TryEnum<ElementSize>(body?["size"], out var size)) r.Size = size;
            if (TryEnum<ElementActivity>(body?["activity"], out var act)) r.Activity = act;
            if (TryEnum<ElementEquipment>(body?["equipment"], out var eq)) r.Equipment = eq;
            if (TryEnum<StatusColour>(body?["strength"], out var st)) r.Strength = st;
            if (TryEnum<StatusColour>(body?["ammo"], out var am)) r.Ammo = am;
            r.Count = (byte)Math.Clamp(Num(body?["count"], kind is ReportKind.Spot or ReportKind.Contact ? 1 : 0), 0, 255);
            r.Phase = (ushort)Math.Clamp(Num(body?["phase"], 0), 0, ushort.MaxValue);
            r.Radius = (float)Num(body?["radius"], 0);
            if (body?["at"] is JsonArray at && at.Count >= 2)
                r.At = new Vector3((float)Num(at[0], 0), (float)Num(at[1], 0), at.Count > 2 ? (float)Num(at[2], 0) : 0f);
            else if (body?["x"] != null || body?["y"] != null)
                r.At = new Vector3((float)Num(body?["x"], 0), (float)Num(body?["y"], 0), (float)Num(body?["z"], 0));
            else if (_world.HasComponent<SimTransform>(unit))
                r.At = _world.GetComponentRO<SimTransform>(unit).Position;
            float ago = (float)Num(body?["observedAgoSeconds"], 0);
            var error = ReportInjection.Inject(_world, unit, r, ago);
            if (error != null) return (null, 409, error);
            return (new JsonObject { ["networkId"] = networkId, ["sent"] = true, ["synthetic"] = true, ["report"] = ReportNode(r, CommandPictureOps.Now(_world), null) }, 200, null);
        }

        /// <summary><c>POST /entities/{networkId}/reports/mute {seconds}</c> — lost comms (CC-15); 0 restores the radio.</summary>
        public (JsonNode? result, int status, string? error) PostEntityReportMute(long networkId, JsonNode? body)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return (null, 404, $"Entity {networkId} not found.");
            float seconds = (float)Num(body?["seconds"], 0);
            var error = ReportInjection.Mute(_world, unit, seconds);
            if (error != null) return (null, 409, error);
            return (new JsonObject { ["networkId"] = networkId, ["mutedSeconds"] = seconds }, 200, null);
        }

        /// <summary><c>GET /squad/reports?last=&amp;unit=</c> — the reports this node's inbox saw, newest first (PC-11).</summary>
        public JsonNode GetSquadReports(int last, long? unitNetId)
        {
            var log = SquadReportLog.Peek(_world);
            if (log == null)
                return new JsonObject { ["count"] = 0, ["reports"] = new JsonArray(), ["note"] = "no report has reached this node's inbox (the staff runs on brain nodes)" };
            long? Id(Entity e) => !e.IsNull && _entityMap.TryGetNetworkId(e, out var id) ? id : null;
            float now = CommandPictureOps.Now(_world);
            var arr = new JsonArray();
            foreach (var e in log.Recent(SquadReportLog.Capacity))
            {
                if (arr.Count >= last) break;
                var from = Id(e.FromUnit);
                var to = Id(e.ToUnit);
                if (unitNetId.HasValue && from != unitNetId && to != unitNetId) continue;
                var n = ReportNode(e.Report, now, null);
                n["serial"] = e.Serial; n["seenAt"] = e.SeenAt; n["from"] = from; n["to"] = to;
                n["originNode"] = e.OriginNodeId; n["verdict"] = e.Verdict;
                arr.Add(n);
            }
            return new JsonObject { ["count"] = log.Count, ["returned"] = arr.Count, ["reports"] = arr };
        }

        private static JsonObject ReportNode(in CommandReport r, float now, float? dueAt)
        {
            var n = new JsonObject
            {
                ["kind"] = r.Kind.ToString(),
                ["subtype"] = CommandReportText.SubtypeName(r),   // ⭐ CE-1191 — shared with the Reports window
                ["at"] = V3(r.At), ["observedAt"] = r.ObservedAt, ["ageSeconds"] = now - r.ObservedAt, ["seq"] = r.Seq,
                ["synthetic"] = r.Synthetic != 0,
            };
            if (CommandReportText.CarriesElementFields(r))
            {
                n["count"] = r.Count; n["size"] = r.Size.ToString(); n["radius"] = r.Radius;
                n["activity"] = r.Activity.ToString(); n["equipment"] = r.Equipment.ToString();
            }
            if (r.Kind is ReportKind.Sitrep or ReportKind.Lace) { n["strength"] = r.Strength.ToString(); n["ammo"] = r.Ammo.ToString(); n["phase"] = r.Phase; }
            if (r.Kind == ReportKind.Progress && (ProgressKind)r.Subtype == ProgressKind.PhaseLine) n["phaseLine"] = r.Count;
            if (dueAt is float d) n["dueAt"] = d;
            return n;
        }

        private static JsonArray V3(Vector3 v) => new() { v.X, v.Y, v.Z };

        private static bool TryEnum<T>(JsonNode? node, out T value) where T : struct, Enum
        {
            value = default;
            return node is JsonValue v && v.TryGetValue<string>(out var s) && Enum.TryParse(s, ignoreCase: true, out value);
        }

        private static double Num(JsonNode? node, double fallback)
        {
            if (node is not JsonValue v) return fallback;
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }
    }
}
