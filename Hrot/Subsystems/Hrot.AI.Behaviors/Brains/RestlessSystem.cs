using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Definitions.Behavior;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Stance;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>Why <see cref="RestlessHolds.Holds"/> keeps a man still (for the rails and the journal).</summary>
    public enum RestlessHold : byte
    {
        None = 0,
        /// <summary>① dead, incapacitated, stunned, embarked (a carried man too) or carrying one.</summary>
        Down,
        /// <summary>② an order to stay: an ordered hold, or a posture told to stay or given a post.</summary>
        OrderedToStay,
        /// <summary>③ a move of his own is under way (a play's, a formation's).</summary>
        Moving,
        /// <summary>④ his weapon is up (aiming or firing) — only a calm shift waits for it.</summary>
        WeaponUp,
        /// <summary>⑤ his stillness is the point: an ambush, the trigger man, the bomber, a window duel.</summary>
        HiddenOnPurpose,
        /// <summary>⑥ an operator drives him.</summary>
        Possessed,
        /// <summary>a fight from cover: its own peeks and hides move him (AQ103 step 3 brings the in-cover shift).</summary>
        FightingFromCover,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2368</c> (AQ103 B, R-2074) — the ONE stay-still rule: every reason a stationary person must NOT shift. 🔒 User: <i>"This is,
    /// of course, not the case when a soldier is part of a formation or has been given some orders to stay in place. This should be just
    /// the default if no such orders exist."</i> ⇒ the default is NOT held. 📄 docs/blueprints/Architect_Question_103_Idle_Life.md §4 B.
    /// </summary>
    public static class RestlessHolds
    {
        private static readonly int IdleHash = BehaviorHash.FromName(BehaviorNames.Idle);

        /// <summary>The behaviours whose stillness is the point (B⑤) — by name, in one list.</summary>
        private static readonly int[] StillOnPurpose =
        {
            BehaviorHash.FromName(BehaviorNames.Ambush), BehaviorHash.FromName(BehaviorNames.TriggerIed),
            BehaviorHash.FromName(BehaviorNames.DetonateCharge), BehaviorHash.FromName(BehaviorNames.WindowDuel),
        };

        /// <summary>Orders to hold (B②): an ORDERED one of these holds (a sentry always); the SOP's own <c>Idle</c> (origin Sop) is the default — not a hold.</summary>
        private static readonly int[] HoldOrders =
        {
            IdleHash, BehaviorHash.FromName(BehaviorNames.PersonHold), BehaviorHash.FromName(BehaviorNames.HoldStance),
            BehaviorHash.FromName(BehaviorNames.HoldTheLine),
            // ⭐ a SENTRY stays on his post (no shift), but is no hidden man: bored, he may smoke (I) — 🔒 user: "waiting in some boring job"
            BehaviorHash.FromName("Sentry"),
        };

        private static readonly int FightFromCoverHash = BehaviorHash.FromName(BehaviorNames.FightFromCover);
        private static readonly int CombatPostureHash = BehaviorHash.FromName(BehaviorNames.CombatPosture);

        /// <summary>
        /// The reason <paramref name="self"/> stays still now, or <see cref="RestlessHold.None"/>. <paramref name="calm"/> false (he is lying
        /// under fire) lets the weapon-up hold go: the pinned man's scoot is made firing (R2-3).
        /// </summary>
        public static RestlessHold Holds(EntityRepository world, Entity self, bool calm = true)
        {
            // ① down
            if (Injury.StateOf(world, self) >= InjuryState.Incapacitated || Effects.IsStunned(world, self)) return RestlessHold.Down;
            if (world.IsComponentTypeRegistered<IsEmbarkedTag>() && world.HasComponent<IsEmbarkedTag>(self)) return RestlessHold.Down;
            if (!Embarkation.LoadOf(world, self).IsNull) return RestlessHold.Down;
            // ③ a move of his own
            if (world.HasComponent<LocomotionChannel>(self))
            {
                ref readonly var ch = ref world.GetComponentRO<LocomotionChannel>(self);
                if (ch.ActiveAction == NavigationConstants.ActionIdMoveTo && ch.Status == Fbt.NodeStatus.Running) return RestlessHold.Moving;
            }
            if (world.IsComponentTypeRegistered<UnderFireMove>() && world.HasComponent<UnderFireMove>(self)
                && world.GetComponentRO<UnderFireMove>(self).ActionInstanceId != 0) return RestlessHold.Moving;
            if (!world.HasComponent<BehaviorState>(self)) return RestlessHold.None;
            var task = world.GetComponentRO<BehaviorState>(self);
            // ⑥ possessed
            if (task.Origin == BehaviorOrigin.Possession) return RestlessHold.Possessed;
            int hash = task.ActiveBehaviorHash;
            // ⑤ still on purpose
            if (Array.IndexOf(StillOnPurpose, hash) >= 0) return RestlessHold.HiddenOnPurpose;
            if (hash == FightFromCoverHash) return RestlessHold.FightingFromCover;
            // ② ordered to stay: an ordered hold (never the SOP's own idle choice), or a posture told to stay / given a post
            if (Array.IndexOf(HoldOrders, hash) >= 0 && (hash != IdleHash || task.Origin != BehaviorOrigin.Sop)) return RestlessHold.OrderedToStay;
            if (hash == CombatPostureHash && PostureStays(world, self, task.InstanceId)) return RestlessHold.OrderedToStay;
            // ④ the weapon up
            if (calm && world.IsComponentTypeRegistered<WeaponPosture>() && world.HasComponent<WeaponPosture>(self)
                && world.GetComponentRO<WeaponPosture>(self).Kind == WeaponPostureKind.Aimed) return RestlessHold.WeaponUp;
            return RestlessHold.None;
        }

        /// <summary>The running <c>CombatPosture</c> was started with <c>stay</c> or a <c>post</c> (its start record's JSON).</summary>
        private static bool PostureStays(EntityRepository world, Entity self, uint instance)
        {
            if (!world.HasManagedComponent<BehaviorStartRecord>(self)) return false;
            var start = ((ISimulationView)world).GetManagedComponentRO<BehaviorStartRecord>(self);
            if (start.InstanceId != instance || string.IsNullOrEmpty(start.JsonParams)) return false;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(start.JsonParams);
                var root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
                foreach (var p in root.EnumerateObject())
                {
                    if (p.NameEquals("stay") || p.NameEquals("Stay"))
                        if (p.Value.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                    if ((p.NameEquals("post") || p.NameEquals("Post")) && p.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                        foreach (var q in p.Value.EnumerateObject())
                            if ((q.NameEquals("radius") || q.NameEquals("Radius")) && q.Value.TryGetSingle(out float r) && r > 0f) return true;
                }
            }
            catch (System.Text.Json.JsonException) { }
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2368</c> (AQ103 A/C/D, R-2074) — idle life: a PERSON who has stood still a while, and whom nothing holds
    /// (<see cref="RestlessHolds"/>), shifts a little now and then — standing or crouched a 0.5–2 m step every seeded 8–20 s; lying, a
    /// 0.5–1.5 m crawl every 10–25 s; lying UNDER FIRE, R2-3's scoot (<see cref="UnderFire.Scoot"/>, 2–4 m square to the shooter, sides
    /// alternating, every 6–12 s) — never past <see cref="TetherMetres"/> from where he settled. Its own move intent (<see cref="MoveToExecutor.WriteIntent"/>),
    /// never the play's channel: a play calling <c>Stop</c> every tick cannot cancel it, and its next move simply replaces it. Vehicles never
    /// (a person is what carries a stance). 🔒 User: <i>"Any entity which is just standing doing nothing is feeling weird, suspicious, not
    /// realistic. If nothing else, it should move a little bit here and there, even if idling."</i>
    /// 📄 docs/blueprints/Architect_Question_103_Idle_Life.md §3–§4.
    /// </summary>
    public static class Restlessness
    {
        /// <summary>Still this long (s) before his first shift.</summary>
        public const double StillSeconds = 6.0;
        /// <summary>Between shifts standing or crouched (s, seeded).</summary>
        public const double StandMinSeconds = 8.0, StandMaxSeconds = 20.0;
        /// <summary>Between shifts lying, calm (s, seeded).</summary>
        public const double LieMinSeconds = 10.0, LieMaxSeconds = 25.0;
        /// <summary>A standing step (m, seeded).</summary>
        public const float StepMinMetres = 0.5f, StepMaxMetres = 2f;
        /// <summary>A calm crawl (m, seeded).</summary>
        public const float CrawlMinMetres = 0.5f, CrawlMaxMetres = 1.5f;
        /// <summary>He never shifts past this far from where he settled (m).</summary>
        public const float TetherMetres = 3f;
        /// <summary>The pace of a shift (m/s) — an idle step; a prone man's stance caps it to a crawl.</summary>
        public const float ShiftSpeed = 1.0f;
        /// <summary>A shift gives up after this long (s).</summary>
        public const double ShiftSeconds = 8.0;
        /// <summary>Moved this far by anything else (m), he re-settles there.</summary>
        public const float ResettleMetres = 0.5f;
        /// <summary>A held man is looked at again after this long (s).</summary>
        public const double HeldRecheckSeconds = 2.0;
        /// <summary>A near miss or hit this recent (s) has him under fire.</summary>
        public const double UnderFireSeconds = 3.0;

        /// <summary>One frame of idle life for <paramref name="self"/> (the system's body; public for the rails).</summary>
        public static void Step(EntityRepository world, Entity self, double now)
        {
            if (!world.HasComponent<SimTransform>(self) || !world.HasComponent<NavigationIntent>(self)) return;
            var at = world.GetComponentRO<SimTransform>(self).Position;
            if (!world.HasComponent<Restless>(self))
            {
                world.AddComponent(self, new Restless { Anchor = at, LastPos = at, StillSince = now, NextAt = now + StillSeconds });
                return;
            }
            ref var r = ref world.GetComponentRW<Restless>(self);
            IdleGesture(world, self, ref r, now);
            if (r.Moving != 0)
            {
                // a shift under way: done when it arrives or ends, given up after its time, or replaced by anything else's move
                uint intent = world.GetComponentRO<NavigationIntent>(self).IntentId;
                if (intent != r.HoldIntentId) { Resettle(ref r, at, now); return; }
                if (!Ended(world, self, r.HoldIntentId) && now - r.StillSince < ShiftSeconds) return;
                r.Moving = 0;
                r.HoldIntentId = 0;
                r.LastPos = at;
                r.StillSince = now;
                r.NextAt = now + Interval(world, self, r.Moves, now);
                return;
            }
            if (Flat(at - r.LastPos).Length() > ResettleMetres) { Resettle(ref r, at, now); return; }   // something else moved him
            if (now < r.NextAt) return;

            bool prone = Stance(world, self) == StanceId.Prone;
            bool underFire = prone && now - UnderFire.LastFire(world, self) <= UnderFireSeconds;
            if (RestlessHolds.Holds(world, self, calm: !underFire) != RestlessHold.None)
            {
                r.NextAt = now + HeldRecheckSeconds;
                r.LastPos = at;
                return;
            }
            var to = underFire ? UnderFire.Scoot(world, self, at, at, r.Moves, r.StillSince) : Shift(self, at, r.Anchor, prone, r.Moves, now);
            if (Flat(to - r.Anchor).Length() > TetherMetres) to = r.Anchor + Clamp(Flat(to - r.Anchor), TetherMetres);
            var move = new MoveToParams
            {
                Destination = to, ArrivalRadius = 0.3f, Speed = underFire ? UnderFire.RushSpeed : ShiftSpeed,
                Flags = NavigationConstants.FlagDestinationOnSurface,
            };
            r.HoldIntentId = MoveToExecutor.WriteIntent(world, self, in move);
            r.Moving = 1;
            r.StillSince = now;   // the shift's own clock (ShiftSeconds)
            r.Moves++;
        }

        /// <summary>⭐ AQ103 I — how long an idle gesture shows (s, seeded), and the pause before the next.</summary>
        public const double GestureMinSeconds = 20.0, GestureMaxSeconds = 60.0, GestureGapMinSeconds = 20.0, GestureGapMaxSeconds = 90.0;
        /// <summary>⭐ AQ103 I — calm: no enemy SEEN for this long (s) and no round near him for <see cref="CalmAfterFireSeconds"/>.</summary>
        public const uint CalmAfterSeenTicks = 600;
        public const double CalmAfterFireSeconds = 30.0;

        /// <summary>The idle moods a man may show, in the order a man's HABIT is drawn from (a smoker smokes).</summary>
        private static readonly GestureKind[] IdleKinds =
            { GestureKind.IdleSmoking, GestureKind.IdleShiftWeight, GestureKind.IdleStretch, GestureKind.IdleLookAbout };

        /// <summary>
        /// ⭐⭐ <c>CE-2368</c> (AQ103 I, R-2074) — a RELAXED man shows an idle gesture the IG animates (smoking, shifting weight, a stretch,
        /// looking about): idle and calm — standing or crouched, no enemy seen lately, no round near him, and held by nothing but at most an
        /// order to stay (a sentry or a man told to hold may be bored too). ⭐ A gesture his BEHAVIOUR set is never touched; his own idle
        /// one goes the moment he is not calm. 🔒 User: <i>"Some of them could be smoking a cigarette, some of them just moving their
        /// bodies, stepping from one leg to the other … And that of course applies to civilians as well"</i>.
        /// </summary>
        public static void IdleGesture(EntityRepository world, Entity self, ref Restless r, double now)
        {
            var current = Gestures.Of(world, self);
            if (current != GestureKind.None && !Gestures.IsIdle(current)) return;   // his behaviour's act
            if (now < r.GestureCheckAt) return;
            r.GestureCheckAt = now + 1.0;
            if (!Calm(world, self, now))
            {
                if (Gestures.IsIdle(current)) Gestures.Set(world, self, GestureKind.None);
                if (r.NextGestureAt < now + GestureGapMinSeconds) r.NextGestureAt = now + GestureGapMinSeconds;
                return;
            }
            if (Gestures.IsIdle(current))
            {
                if (now < r.GestureUntil) return;
                Gestures.Set(world, self, GestureKind.None);
                r.NextGestureAt = now + Seeded(self, 0x236A, now, GestureGapMinSeconds, GestureGapMaxSeconds);
                return;
            }
            if (r.NextGestureAt <= 0) { r.NextGestureAt = now + Seeded(self, 0x236B, now, 10.0, 40.0); return; }
            if (now < r.NextGestureAt) return;
            Gestures.Set(world, self, PickIdle(self, now));
            r.GestureUntil = now + Seeded(self, 0x236C, now, GestureMinSeconds, GestureMaxSeconds);
        }

        /// <summary>Idle and calm enough for an idle gesture (<see cref="IdleGesture"/>).</summary>
        public static bool Calm(EntityRepository world, Entity self, double now)
        {
            var hold = RestlessHolds.Holds(world, self);
            if (hold != RestlessHold.None && hold != RestlessHold.OrderedToStay) return false;
            if (Stance(world, self) == StanceId.Prone) return false;
            if (now - UnderFire.LastFire(world, self) <= CalmAfterFireSeconds) return false;
            return !CoverFightConditions.SeenWithin(self, world, CalmAfterSeenTicks);
        }

        /// <summary>His idle mood this time: his HABIT (seeded by who he is) six times in ten, another the rest.</summary>
        public static GestureKind PickIdle(Entity self, double now)
        {
            var habit = IdleKinds[(int)(Fdp.Toolkit.Determinism.SimRng.FromSim(self.Index, 0x236D, 0f).NextSingle() * IdleKinds.Length) % IdleKinds.Length];
            var rng = Fdp.Toolkit.Determinism.SimRng.FromSim(self.Index, 0x236E, (float)now);
            if (rng.NextSingle() < 0.6f) return habit;
            return IdleKinds[(int)(rng.NextSingle() * IdleKinds.Length) % IdleKinds.Length];
        }

        private static double Seeded(Entity self, int salt, double now, double min, double max)
            => min + Fdp.Toolkit.Determinism.SimRng.FromSim(self.Index, salt, (float)now).NextSingle() * (max - min);

        /// <summary>Where a calm shift goes: a seeded bearing and length; past half the tether he turns back toward where he settled.</summary>
        public static Vector3 Shift(Entity self, Vector3 at, Vector3 anchor, bool prone, byte moves, double now)
        {
            var rng = Fdp.Toolkit.Determinism.SimRng.FromSim(self.Index, 0x2368, (float)now);
            float min = prone ? CrawlMinMetres : StepMinMetres, max = prone ? CrawlMaxMetres : StepMaxMetres;
            float metres = min + rng.NextSingle() * (max - min);
            float bearing = rng.NextSingle() * MathF.PI * 2f;
            var dir = new Vector3(MathF.Cos(bearing), MathF.Sin(bearing), 0f);
            var home = Flat(anchor - at);
            if (home.Length() > 0.5f * TetherMetres) dir = Vector3.Normalize(home);   // back toward where he settled
            return at + dir * metres;
        }

        /// <summary>The wait before his next shift (s): seeded per man and per shift; lying longer than standing.</summary>
        public static double Interval(EntityRepository world, Entity self, byte moves, double now)
        {
            var rng = Fdp.Toolkit.Determinism.SimRng.FromSim(self.Index, 0x2369 + moves, (float)now);
            bool prone = Stance(world, self) == StanceId.Prone;
            if (prone && now - UnderFire.LastFire(world, self) <= UnderFireSeconds) return UnderFire.ScootAfter(self, now);
            double min = prone ? LieMinSeconds : StandMinSeconds, max = prone ? LieMaxSeconds : StandMaxSeconds;
            return min + rng.NextSingle() * (max - min);
        }

        private static void Resettle(ref Restless r, Vector3 at, double now)
        {
            r.Anchor = at;
            r.LastPos = at;
            r.StillSince = now;
            r.NextAt = now + StillSeconds;
            r.Moving = 0;
            r.HoldIntentId = 0;
            r.Moves = 0;
        }

        private static StanceId Stance(EntityRepository world, Entity self)
            => world.HasComponent<StanceIntent>(self) ? world.GetComponentRO<StanceIntent>(self).TargetStance : StanceId.Standing;

        private static bool Ended(EntityRepository world, Entity self, uint hold)
        {
            if (hold == 0 || !world.IsComponentTypeRegistered<NavigationStatus>() || !world.HasComponent<NavigationStatus>(self)) return false;
            ref readonly var s = ref world.GetComponentRO<NavigationStatus>(self);
            return s.IntentId == hold && s.Result != NavigationResult.InProgress && s.Result != NavigationResult.PathFound;
        }

        private static Vector3 Clamp(Vector3 v, float max) => v.Length() > max ? Vector3.Normalize(v) * max : v;
        private static Vector3 Flat(Vector3 v) => new(v.X, v.Y, 0f);
    }

    /// <summary>
    /// ⭐ <c>CE-2368</c> (AQ103 §3.3) — runs <see cref="Restlessness.Step"/> for every PERSON (an entity that carries a stance) on the brain
    /// host, after <see cref="UnderFireMoveSystem"/>. Gated on the brain's authority, as the reflex beside it.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [UpdateAfter(typeof(UnderFireMoveSystem))]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class RestlessSystem : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        private EntityQuery? _people;

        public RestlessSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Restless>() || !repo.IsComponentTypeRegistered<StanceIntent>()) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0.0;
            _people ??= repo.Query().With<StanceIntent>().With<NavigationIntent>().With<SimTransform>().Build();
            foreach (var e in _people)
            {
                if (_gateOnAuthority && !(repo.HasComponent<BehaviorState>(e) && repo.HasAuthority<BehaviorState>(e))) continue;
                if (!repo.HasComponent<BehaviorState>(e)) continue;   // a brain, not a bare body
                Restlessness.Step(repo, e, now);
            }
        }
    }
}
