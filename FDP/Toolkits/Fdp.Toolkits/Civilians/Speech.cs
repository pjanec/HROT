using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Civilians
{
    /// <summary>
    /// ⭐ <c>CE-3264</c> (G1, SP-2; docs/DESIGN_Civilians_And_Crowd_Control.md §7.18) — WHAT a man says. G1's five (talk, insult, warning, order,
    /// chant) and Building Clearing CB-27's challenge, commands and calls (R-2033), appended in that order; it crosses as a byte.
    /// ⚠ Member names unique (one IDL scope).
    /// </summary>
    public enum UtteranceKind : byte
    {
        UtterNone = 0,
        Talk = 1,
        Insult = 2,
        Warning = 3,
        /// <summary>A police / forces order to a crowd ("disperse").</summary>
        Order = 4,
        Chant = 5,
        /// <summary>CB-27 — the challenge ("police, show me your hands").</summary>
        Challenge = 6,
        CmdDown = 7,
        CmdFreeze = 8,
        CmdHandsUp = 9,
        CmdDropIt = 10,
        CmdComeOut = 11,
        /// <summary>CB-27 — announcing at the door ("police, search warrant").</summary>
        Announce = 12,
        /// <summary>CB-27 — the team call "clear".</summary>
        CallClear = 13,
        /// <summary>CB-27 — the team call "man down".</summary>
        CallManDown = 14,
    }

    /// <summary>
    /// ⭐ <c>CE-3264</c> (G1, SP-1 / SP-3) — what a man is SAYING now: on the speaker, Brain group, replicated as <c>dtUtterance</c> (152) with
    /// <see cref="Target"/> as a network id. One at a time (the newest replaces); over when sim time reaches <see cref="Until"/>. Written
    /// only through <see cref="Utterances.Say"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Utterance)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Utterance
    {
        public UtteranceKind Kind;
        /// <summary>Whom he addresses (a command's target); <see cref="Entity.Null"/> = everyone around.</summary>
        public Entity Target;
        /// <summary>Sim time (s) the utterance is over.</summary>
        public double Until;
    }

    /// <summary>⭐ <c>CE-3264</c> — the one writer and the readers of <see cref="Utterance"/>. The POLICY (who says what, when) is behaviors'
    /// (§5o, <c>CE-2249</c>); this is the data's single door.</summary>
    public static class Utterances
    {
        /// <summary><paramref name="man"/> says <paramref name="kind"/> to <paramref name="target"/> for <paramref name="seconds"/> from
        /// <paramref name="now"/>; call it on the node that owns his brain (and so <c>dtUtterance</c>). False when the type is not registered
        /// or he is gone.</summary>
        public static bool Say(EntityRepository repo, Entity man, UtteranceKind kind, Entity target, double now, float seconds)
        {
            if (!repo.IsAlive(man) || !repo.IsComponentTypeRegistered<Utterance>()) return false;
            var u = new Utterance { Kind = kind, Target = target, Until = now + System.Math.Max(0f, seconds) };
            if (repo.HasComponent<Utterance>(man)) repo.GetComponentRW<Utterance>(man) = u;
            else repo.AddComponent(man, u);
            return true;
        }

        /// <summary>What <paramref name="man"/> is saying at <paramref name="now"/>; <see cref="UtteranceKind.UtterNone"/> when nothing (or it
        /// is over).</summary>
        public static UtteranceKind Of(ISimulationView view, Entity man, double now, out Entity target)
        {
            target = Entity.Null;
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<Utterance>()) return UtteranceKind.UtterNone;
            if (!view.IsAlive(man) || !view.HasComponent<Utterance>(man)) return UtteranceKind.UtterNone;
            ref readonly var u = ref view.GetComponentRO<Utterance>(man);
            if (now >= u.Until) return UtteranceKind.UtterNone;
            target = u.Target;
            return u.Kind;
        }

        /// <summary>
        /// ⭐ CB-27 — the newest live utterance ADDRESSED to <paramref name="target"/> (its <see cref="Utterance.Target"/> is him): what the
        /// target's brain answers. False when nobody is speaking to him.
        /// </summary>
        public static bool Addressed(ISimulationView view, Entity target, double now, out Entity speaker, out UtteranceKind kind)
        {
            speaker = Entity.Null; kind = UtteranceKind.UtterNone;
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<Utterance>()) return false;
            double best = double.MinValue;
            foreach (var e in view.Query().With<Utterance>().Build())
            {
                ref readonly var u = ref view.GetComponentRO<Utterance>(e);
                if (now >= u.Until || u.Target != target || u.Until <= best) continue;
                best = u.Until; speaker = e; kind = u.Kind;
            }
            return kind != UtteranceKind.UtterNone;
        }
    }
}
