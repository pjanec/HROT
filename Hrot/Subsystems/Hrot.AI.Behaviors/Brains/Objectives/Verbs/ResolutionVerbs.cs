using System;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules.Actions;
using Fdp.Toolkit.Weather;

namespace Hrot.AI.Behaviors.Brains.Objectives.Verbs
{
    /// <summary>
    /// ⭐ CE-8300 (ST-5, §16b) — <c>SetResolutionPolicy</c>'s params. ⭐ PARTIAL: a number left 0 keeps what the policy holds (a number cannot be set to
    /// 0 — ask for a tiny positive value); <see cref="Area"/> ADDS one detail area (SV-15, ≤ 8), <see cref="ClearAreas"/> forgets them all first.
    /// </summary>
    public struct SetResolutionPolicyParams : IActionArgsCheck
    {
        public float PhysicalBand, LeadMetres, MergeMargin, ForcedRange, FrameBudgetMs;
        /// <summary>⭐ S0 (CE-8304) — lengths of sim time, as on the component; the JSON is the same number of seconds.</summary>
        public SimDuration QuietSeconds, DeadlineSeconds;
        /// <summary>A detail area, kept AS AUTHORED on the component (CE-8304, R-8380); a bare id is stored as the target's GUID.</summary>
        public EntityLink Area;
        public bool ClearAreas;

        public readonly string? Check(JsonObject args)
        {
            foreach (var (k, v) in args)
            {
                if (v is JsonValue jv && jv.TryGetValue<double>(out double d) && d < 0 && !string.Equals(k, "area", StringComparison.OrdinalIgnoreCase))
                    return $"{k} {d.ToString(System.Globalization.CultureInfo.InvariantCulture)} is negative";
            }
            return null;
        }
    }

    /// <summary>
    /// ⭐⭐ CE-8300 (ST-5, §16b R-1…R-7) — <c>SetResolutionPolicy</c>: sets the resolution manager's bands / timings / detail areas on the World
    /// Environment's <see cref="ResolutionPolicy"/> (the same authoring path as <see cref="SkillVerbs.SetSkillAmplifier"/>; time 0 or later — "phase 2:
    /// the town becomes a detail area"). The manager reads it at its next step. Instant. The write lands serially on the LIVE component
    /// (<see cref="RuleAfterPhase"/>), so two settings in one tick both land.
    /// </summary>
    public static class ResolutionVerbs
    {
        private sealed class Log { }

        [SharedAiAction]
        public static NodeStatus SetResolutionPolicy(ref SetResolutionPolicyParams p, ref VerbNoState s, Entity self, EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<ResolutionPolicy>() || !world.IsComponentTypeRegistered<ScenarioClock>())
                return Fail(world, self, "no World Environment on this node");
            Entity env = Entity.Null;
            foreach (var e in world.Query().With<ScenarioClock>().WithLifecycle(EntityLifecycle.All).Build()) { env = e; break; }
            if (env.IsNull) return Fail(world, self, "no World Environment");
            var args = p;
            var area = p.Area.Ref;
            if (!area.IsNone && area.Resolve(world).IsNull) return Fail(world, self, "the detail area names no entity here");
            RuleAfterPhase.Run(world, self, w =>
            {
                if (!w.IsAlive(env)) return;
                var live = w.HasComponent<ResolutionPolicy>(env) ? w.GetComponentRO<ResolutionPolicy>(env) : default;
                var serial = args;
                serial.Area = Authored(w, args.Area);   // the target's GUID is read here, serially (no cross-entity read in the rule phase)
                Apply(ref live, in serial);
                if (w.HasComponent<ResolutionPolicy>(env)) w.SetComponent(env, live); else w.AddComponent(env, live);
                var b = live.Bands();
                Fdp.Core.Logging.FdpLog<Log>.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"resolution policy: band {b.PhysicalBand} m, lead {b.LeadMetres} m, margin {b.MergeMargin} m, quiet {(float)b.QuietSeconds} s, forced {b.ForcedRange} m, {live.AreaCount} detail area(s) (asked by {self})"));
            });
            return NodeStatus.Success;
        }

        /// <summary>The partial merge — one place, so the verb and a rail say the same thing.</summary>
        public static void Apply(ref ResolutionPolicy live, in SetResolutionPolicyParams p)
        {
            if (p.PhysicalBand > 0f) live.PhysicalBand = p.PhysicalBand;
            if (p.LeadMetres > 0f) live.LeadMetres = p.LeadMetres;
            if (p.MergeMargin > 0f) live.MergeMargin = p.MergeMargin;
            if (p.QuietSeconds > 0f) live.QuietSeconds = p.QuietSeconds;
            if (p.ForcedRange > 0f) live.ForcedRange = p.ForcedRange;
            if (p.DeadlineSeconds > 0f) live.DeadlineSeconds = p.DeadlineSeconds;
            if (p.FrameBudgetMs > 0f) live.FrameBudgetMs = p.FrameBudgetMs;
            if (p.ClearAreas) live.ClearAreas();
            if (!p.Area.IsNone) live.AddArea(p.Area);
        }

        /// <summary>⭐ CE-8304 (R-8380) — the link the component keeps: as authored (a GUID / <c>@Key</c>); a BARE id (a live pick, an id typed in)
        /// becomes the target's stable GUID, so the saved scenario never holds a network id (S9′) — unless the target has none (then as given).</summary>
        public static EntityLink Authored(EntityRepository world, EntityLink area)
        {
            if (area.Ref.IsNone || !area.Spec.IsNone || !world.IsComponentTypeRegistered<Fdp.Toolkit.Scenario.EntityGuid>()) return area;
            var e = area.Ref.Resolve(world);
            if (e.IsNull || !world.HasComponent<Fdp.Toolkit.Scenario.EntityGuid>(e)) return area;
            var guid = world.GetComponentRO<Fdp.Toolkit.Scenario.EntityGuid>(e).Value;
            return guid == Guid.Empty ? area : new EntityLink(area.Ref, RefSpec.ByExact(guid));
        }

        private static NodeStatus Fail(EntityRepository world, Entity self, string reason)
        {
            ActionFailure.Set(world, self, reason);
            return NodeStatus.Failure;
        }
    }
}
