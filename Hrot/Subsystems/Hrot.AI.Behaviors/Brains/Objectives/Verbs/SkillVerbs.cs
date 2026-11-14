using System;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.ScenarioRules.Actions;
using Fdp.Toolkit.Weather;

namespace Hrot.AI.Behaviors.Brains.Objectives.Verbs
{
    /// <summary>⭐ <c>SetSkillAmplifier</c>'s params (macros S3b B3): which side, and its amplifier in tiers (−2 … +2; 0 = his own level).</summary>
    public struct SetSkillAmplifierParams : IActionArgsCheck
    {
        public ForceId Side;
        public int Tiers;

        public readonly string? Check(JsonObject args)
        {
            foreach (var (k, v) in args)
            {
                if (string.Equals(k, "tiers", StringComparison.OrdinalIgnoreCase) && v is JsonValue tv && tv.TryGetValue<int>(out int t)
                    && Math.Abs(t) > SkillAmplifier.MaxTiers)
                    return $"tiers {t} is outside -{SkillAmplifier.MaxTiers} … +{SkillAmplifier.MaxTiers}";
            }
            return null;
        }
    }

    /// <summary>
    /// ⭐⭐ Macros S3b B3 (SR-100 / R-4106 / P-5) — <c>SetSkillAmplifier(side, tiers)</c>: sets the side's AI skill amplifier on the World Environment
    /// (<see cref="SkillAmplifier"/>); every man of that side reads his own level moved by it from the next tick (<c>ProficiencyFillSystem</c> →
    /// <see cref="CombatProficiency.SideTiers"/> → <see cref="ProficiencyOf.Amplify"/>). ⭐ An amplifier, never a leveller: 0 restores everyone exactly.
    /// Instant. ⚠ Like the other verbs it writes on THIS node (the World Environment's owner — the Brain today).
    /// </summary>
    public static class SkillVerbs
    {
        private sealed class Log { }

        [SharedAiAction]
        public static NodeStatus SetSkillAmplifier(ref SetSkillAmplifierParams p, ref VerbNoState s, Entity self, EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<SkillAmplifier>() || !world.IsComponentTypeRegistered<ScenarioClock>())
                return Fail(world, self, "no World Environment on this node");
            Entity env = Entity.Null;
            foreach (var e in world.Query().With<ScenarioClock>().WithLifecycle(EntityLifecycle.All).Build()) { env = e; break; }
            if (env.IsNull) return Fail(world, self, "no World Environment");

            var amp = world.HasComponent<SkillAmplifier>(env) ? world.GetComponentRO<SkillAmplifier>(env) : default;
            if (!amp.Set(p.Side, p.Tiers)) return Fail(world, self, $"side '{p.Side}' has no amplifier");
            var side = p.Side; var tiers = p.Tiers;
            // ⭐ CE-3448 — the World Environment is ANOTHER entity: the set lands serially, on the LIVE amplifier (two sides set in one tick both land)
            Hrot.AI.Behaviors.Brains.Objectives.RuleAfterPhase.Run(world, self, w =>
            {
                if (!w.IsAlive(env)) return;
                var live = w.HasComponent<SkillAmplifier>(env) ? w.GetComponentRO<SkillAmplifier>(env) : default;
                if (!live.Set(side, tiers)) return;
                if (w.HasComponent<SkillAmplifier>(env)) w.SetComponent(env, live); else w.AddComponent(env, live);
                Fdp.Core.Logging.FdpLog<Log>.Info($"skill amplifier: {side} {live.TiersOf(side):+0;-0;0} tier(s) (asked by {self})");
            });
            return NodeStatus.Success;
        }

        private static NodeStatus Fail(EntityRepository world, Entity self, string reason)
        {
            ActionFailure.Set(world, self, reason);
            return NodeStatus.Failure;
        }
    }
}
