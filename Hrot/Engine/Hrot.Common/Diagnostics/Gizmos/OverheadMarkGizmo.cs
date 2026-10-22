using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>⭐ <c>CE-1176</c> / <c>CE-1177</c> (docs/DESIGN_People_Display.md §5 U15, U16) — the ONE small mark above a person.</summary>
    public enum MarkKind : byte
    {
        None,
        /// <summary>U15 — what he says: a red <c>#!</c>, a yellow <c>/!\</c>, a white <c>!&gt;</c>, a grey <c>...</c>, a cyan <c>~~</c>.</summary>
        Insult, Warning, Order, Talk, Chant,
        /// <summary>U16 — how he feels, a small dot: pain (Injury wounded), anger, fear, alarm (Demeanour).</summary>
        Pain, Anger, Fear, Alarm,
    }

    /// <summary>
    /// ⭐ <c>CE-1176</c> / <c>CE-1177</c> (People Display §5 U15, U16; R-1005) — THE priority rule, so marks never stack: speech first (the
    /// replicated <see cref="Utterance"/>, G1; the police warning <c>Gesture.Warning</c> counts as a warning), then his state — pain, anger,
    /// fear, alarm (<see cref="Injury"/>, the replicated <see cref="Demeanour"/>, G2). Calm and silent: none. Pure; every node gives the
    /// same answer.
    /// </summary>
    public static class Marks
    {
        /// <summary>A speech mark fades over its last half second.</summary>
        public const float FadeSeconds = 0.5f;

        /// <summary>Anger / fear from this quarter up shows (2 = "clearly", Demeanour's quarters 0 … 4).</summary>
        public const byte MoodQuarter = 2;

        /// <summary>The mark on <paramref name="man"/> at sim time <paramref name="now"/>, and its opacity 0 … 1.</summary>
        public static (MarkKind Kind, float Alpha) Of(EntityRepository repo, Entity man, double now)
        {
            var said = Utterances.Of(repo, man, now, out _);
            if (said != UtteranceKind.UtterNone)
            {
                double until = repo.GetComponentRO<Utterance>(man).Until;
                return (SpeechOf(said), (float)System.Math.Clamp((until - now) / FadeSeconds, 0.0, 1.0));
            }
            if (Gestures.Of(repo, man) == GestureKind.Warning) return (MarkKind.Warning, 1f);
            var mood = MoodOf(repo, man);
            return (mood, mood == MarkKind.None ? 0f : 1f);
        }

        /// <summary>His state alone — pain, anger, fear, alarm, or none — whatever he says: the FACE shows it close up (U16) even while the
        /// mark above him is his speech.</summary>
        public static MarkKind MoodOf(EntityRepository repo, Entity man)
        {
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() && Injury.StateOf(repo, man) == InjuryState.Wounded)
                return MarkKind.Pain;
            if (Demeanours.Of(repo, man, out var d))
            {
                if (d.Anger >= MoodQuarter) return MarkKind.Anger;
                if (d.Fear >= MoodQuarter) return MarkKind.Fear;
                if (d.Mode is CivilianMode.Alarmed or CivilianMode.Panic) return MarkKind.Alarm;
            }
            return MarkKind.None;
        }

        /// <summary>G1's kinds onto the five marks: a challenge, the commands and the door announcement are ORDERS; the team calls are TALK.</summary>
        public static MarkKind SpeechOf(UtteranceKind kind) => kind switch
        {
            UtteranceKind.Insult => MarkKind.Insult,
            UtteranceKind.Warning => MarkKind.Warning,
            UtteranceKind.Chant => MarkKind.Chant,
            UtteranceKind.Talk or UtteranceKind.CallClear or UtteranceKind.CallManDown => MarkKind.Talk,
            UtteranceKind.UtterNone => MarkKind.None,
            _ => MarkKind.Order,   // Order, Challenge, Cmd*, Announce
        };

        /// <summary>The mark's glyph (ASCII: the map's font) — a dot for a mood.</summary>
        public static string Glyph(MarkKind kind) => kind switch
        {
            MarkKind.Insult => "#!",
            MarkKind.Warning => "/!\\",
            MarkKind.Order => "!>",
            MarkKind.Talk => "...",
            MarkKind.Chant => "~~",
            MarkKind.None => "",
            _ => "o",
        };

        /// <summary>The mark's colour: insult red, warning yellow, order white, talk grey, chant cyan · pain pink, anger red, fear amber,
        /// alarm white.</summary>
        public static Rgba32 Colour(MarkKind kind, float alpha = 1f)
        {
            var (r, g, b) = kind switch
            {
                MarkKind.Insult => (235, 50, 40),
                MarkKind.Warning => (250, 215, 40),
                MarkKind.Order => (245, 245, 245),
                MarkKind.Talk => (170, 170, 170),
                MarkKind.Chant => (80, 210, 230),
                MarkKind.Pain => (240, 120, 170),
                MarkKind.Anger => (230, 40, 30),
                MarkKind.Fear => (250, 170, 30),
                _ => (240, 240, 240),
            };
            return new Rgba32((byte)r, (byte)g, (byte)b, (byte)(255 * System.Math.Clamp(alpha, 0f, 1f)));
        }

        /// <summary>A mood mark (a dot) rather than speech.</summary>
        public static bool IsMood(MarkKind kind) => kind >= MarkKind.Pain;
    }

    /// <summary>
    /// ⭐ <c>CE-1176</c> / <c>CE-1177</c> (People Display §5 U15, U16) — the one small mark above a person, as a ROW of his entity card: the
    /// card sits above his head in 3-D and above his symbol in 2-D (Map 3-D §3.6), so the mark is where the design puts it on every host with
    /// no layer of its own. Speech at 14 px, a mood dot at 12 px — never more than 20 px. On the Labels layer (on by default — the toggle).
    /// Auto-discovered on every map host (<c>GizmoReflectionRegistrar</c>).
    /// </summary>
    [GizmoProjector(typeof(SimTransform))]
    public sealed class OverheadMarkGizmo : IStatelessGizmo
    {
        /// <summary>The mark's row in the card: above the effect line (<see cref="EffectStateGizmo.Row"/>), below the name rows.</summary>
        public const byte Row = 45;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (view is not EntityRepository repo || !PhysicsColliderReaders.IsPerson(repo, entity)) return;
            if (!repo.IsComponentTypeRegistered<NetworkIdentity>() || !view.HasComponent<NetworkIdentity>(entity)) return;   // a card is keyed by it
            if (CombatLife.IsDestroyed(repo, entity)) return;
            var (kind, alpha) = Marks.Of(repo, entity, HitModel.Now(repo));
            if (kind == MarkKind.None || alpha <= 0f) return;
            long net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0) return;
            draw.Card(net, DebugTraceLayers.Labels).Row(Row).Text(Marks.Glyph(kind), Marks.Colour(kind, alpha), Marks.IsMood(kind) ? 12f : 14f);
        }
    }
}
