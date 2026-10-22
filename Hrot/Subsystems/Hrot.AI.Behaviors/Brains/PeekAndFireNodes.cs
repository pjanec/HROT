using Fdp.Toolkit.Determinism;   // ⭐ CE-2188 — SimRng moved to Fdp.Toolkits
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using FDP.Eqs;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Utility;
using Hrot.MuscleCharacter.Animation.Stance;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-3136</c> P-6 (D7) — how the unit exposes itself.</summary>
    public enum PeekMode : byte
    {
        /// <summary>⭐ <c>CE-3158</c> G1 — from the POINT (<see cref="EqsResult.Kind"/>): a window ⇒ <see cref="Stance"/> at its stance; a
        /// cover ⇒ hide at its stance and come up one stance taller; a cover already standing, a point that is not cover, or a
        /// unit with no stance ⇒ <see cref="Step"/>. ⛔ SUPERSEDED (CE-3136 D7): keyed on the hide TEMPLATE, so a window found by
        /// another query peeked wrong.</summary>
        Auto = 0,
        /// <summary>The same spot: hide low (prone under the sill), expose at the point's stance.</summary>
        Stance = 1,
        /// <summary>Hide behind cover, step out to a nearby point that sees the enemy.</summary>
        Step = 2,
    }

    /// <summary>⭐ <c>CE-3158</c> G8 — what a designer can switch OFF (a zero parameter means "the default", so "off" needs its own bit).</summary>
    [Flags]
    public enum PeekDisable : byte
    {
        None = 0,
        /// <summary>Never fire a blind burst: not seen within the grace time ⇒ back down.</summary>
        BlindFire = 1,
        /// <summary>Warm positions are not scored down (burned ones are still left).</summary>
        HeatPenalty = 2,
        /// <summary>Never suppress before a bound, whatever <c>SuppressBeforeRelocate</c> says.</summary>
        SuppressAndBound = 4,
    }

    /// <summary>
    /// ⭐ <c>CE-3136</c> P-6 — the designer-edited parameters of <see cref="PeekAndFireNodes.PeekAndFire"/> (📄 §8.4). ⭐ A zero means
    /// "the default" (<see cref="PeekAndFireNodes.Effective"/>), so a blackboard nobody filled flies the duel's defaults.
    /// ⚠ The aim time is NOT here: it is the weapon's (D3), so every behaviour aims alike.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PeekAndFireParams
    {
        /// <summary>The cover sensor's template — <see cref="FindWindowFiringPosition"/> (a window) or <see cref="FindCoverFromTarget"/>.</summary>
        public uint HideTemplate;
        /// <summary>How far the cover query looks (m) · how far round the hide point the step-peek query looks (m).</summary>
        public float SearchRadius, PeekSearchRadius;
        /// <summary>See <see cref="PeekMode"/>.</summary>
        public PeekMode Mode;
        /// <summary>The hide stance: 0 = from the point (prone under a window for a stance peek), else <c>StanceId + 1</c>.</summary>
        public byte HideStanceOverride;
        /// <summary>A random wait in this range while hidden (s).</summary>
        public float HideSecondsMin, HideSecondsMax;
        /// <summary>The longest an exposure lasts (s) · the time to SEE the enemy before a blind burst instead (s).</summary>
        public float ExposeSeconds, GraceSeconds;
        /// <summary>A near miss or a hit this recent keeps the unit down (s) (B8, D12).</summary>
        public float SuppressedSeconds;
        /// <summary>Aimed rounds per exposure (B7's count) · rounds of a blind burst.</summary>
        public int RoundsPerExposure, BlindRounds;
        /// <summary>Seconds between rounds.</summary>
        public float FireCooldownSeconds;
        /// <summary>A blind burst flies at the remembered spot raised by this much (m) — the body's middle, not the feet.</summary>
        public float BlindAimHeight;
        /// <summary>Exposures from one hide point before relocating (B4's heat may relocate sooner).</summary>
        public int ExposuresPerPosition;
        /// <summary>B2 — heat an exposure adds · heat being near-missed or hit while exposed adds.</summary>
        public float HeatPerExposure, HeatWhenFiredUpon;
        /// <summary>B4 — burned at · usable again below · a usable warm spot's score penalty per unit of heat.</summary>
        public float BurnHeat, ReuseHeat, HeatPenalty;
        /// <summary>B2/B3 — heat halves in this many seconds · B5 — a candidate within this of a slot IS that slot (m).</summary>
        public float CoolHalfLifeSeconds, MatchRadius;
        /// <summary>Travel speed (m/s) · a new hide point at least this far from the current one (m).</summary>
        public float RelocateSpeed, MinRelocateMetres;
        /// <summary>D12 — 1 = a suppressive burst before relocating (built with P-7).</summary>
        public byte SuppressBeforeRelocate;
        /// <summary>Which forces count as threats for the queries' exposure scoring (0 = every acquired contact).</summary>
        public uint FactionFilter;
        /// <summary>The sensors publish a new answer only when a top score moved by more than this.</summary>
        public float ScoreDeltaThreshold;
        /// <summary>⭐ <c>CE-3158</c> G1 — the stance a STANCE peek comes up to: 0 = from the point (a window's stance; one taller than a
        /// cover's), else <c>StanceId + 1</c> — e.g. "crouch behind a low wall, stand to fire".</summary>
        public byte PeekStanceOverride;
        /// <summary>⭐ <c>CE-3158</c> G2 — with no usable cover point this long the node answers Failure (the posture re-scores);
        /// a step peek with no step-out point this long moves to the next cover (s).</summary>
        public float NoCoverSeconds;
        /// <summary>⭐ <c>CE-3158</c> G4 — a heard shot moves a burst only when it is within this of the locked target's remembered spot
        /// (m); a shot heard elsewhere is another enemy, not this one moving.</summary>
        public float HeardMatchRadius;
        /// <summary>⭐ <c>CE-3158</c> G8 — see <see cref="PeekDisable"/>.</summary>
        public PeekDisable Disable;
        /// <summary>⭐ <c>CE-2275</c> D-2a (PC-14, R-2047) — his POST: with <see cref="AnchorRadius"/> &gt; 0 a hide point must lie within it of
        /// <see cref="Anchor"/>, or be his alternate (<see cref="AnchorAlternate"/>, within <see cref="MatchRadius"/>, when
        /// <see cref="AnchorHasAlternate"/> is 1). 0 = no post: anywhere the sensor answers. 📄 DESIGN_Defend_And_Attack_Position.md §8c D-2a.</summary>
        public Vector3 Anchor, AnchorAlternate;
        public float AnchorRadius;
        public byte AnchorHasAlternate;
        /// <summary>⭐ <c>CE-2275</c> D-2l (DA-77…DA-84, R-2059) — 1 = a FRAME post: he hides STANDING beside the window frame at
        /// <see cref="FrameA"/> or <see cref="FrameB"/> (the one away from the target) and exposes by LEANING out toward
        /// <see cref="Opening"/> (<see cref="Leans"/>); a failed move there drops him to the shape above (DA-84).
        /// 📄 DESIGN_Defend_And_Attack_Position.md §8c D-2l.</summary>
        public byte Frame;
        public Vector3 FrameA, FrameB, Opening;
    }

    /// <summary>
    /// ⭐ <c>CE-3136</c> P-6 — the node's working state (📄 §8.3): scratch, ⛔ lost on every exit by design — what must outlive an
    /// exit lives in <see cref="FiringPositionMemory"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PeekAndFireState
    {
        public PeekPhase Phase;
        /// <summary>1 = a step peek (else a stance peek) · 1 while stepping out to the peek point · 1 once this exposure was counted.</summary>
        public byte PeekIsStep, Stepping, Counted;
        /// <summary>1 while a move this node issued runs.</summary>
        public byte Moving;
        public StanceId HideStance, PeekStance;
        /// <summary>When the current phase's timer ends · when the unit came up · when the enemy was last seen (sim s).</summary>
        public double PhaseUntil, ExposedAt, LastSeenAt;
        public Vector3 HidePoint, PeekPoint;
        /// <summary>The hide-point sensor (<see cref="PeekAndFireParams.HideTemplate"/>) · the step-peek sensor.</summary>
        public EqsSensorHandle CoverSensor, PeekSensor;
        /// <summary>What both sensors are pointed at (an entity, or a heard contact and its point).</summary>
        public Entity Threat;
        public long HeardId;
        public Vector3 HeardPoint;
        /// <summary>Exposures from the current hide point · exposures of this run (the random wait's salt).</summary>
        public int ExposuresHere, Exposures;
        /// <summary>The aimed fire (the posture's ONE fire step) · the blind burst (<see cref="FireAtPointNodes.FireAtPoint"/>).</summary>
        public EngageState Fire;
        public FireAtPointNodeParams Blind;
        /// <summary>⭐ P-7 D12 — 1 while suppressing before a bound: the next cover is picked (<see cref="NextHide"/>), the unit is up
        /// firing a suppressive burst, and on the way down it runs THERE, not back to the old hide point.</summary>
        public byte Bounding;
        /// <summary>The next cover's stance (<see cref="StanceId"/> + 1; 0 = none) · its kind (<see cref="EqsResult.Kind"/>'s encoding).</summary>
        public byte NextStance, NextKind;
        public Vector3 NextHide;
        /// <summary>⭐ <c>CE-3144</c> (P-8 D14) — where both sensors look: the memory slot (its id) and the spot they were pointed at.</summary>
        public long SensorId;
        public Vector3 SensorAt;
        /// <summary>⭐ <c>CE-3158</c> G2 — 1 while the node waits for a point (no cover answer, or no step-out point) · since when (sim s).
        /// 2 = every answered cover point is burned: the unit fires from where it stands meanwhile.</summary>
        public byte Waiting;
        public double WaitingSince;
        /// <summary>⭐ <c>CE-2274</c> DA-C — the target he has already shot at · the new one he is still reacting to, since when (sim s).</summary>
        public Entity Engaged, Reacting;
        public double ReactingSince;
        /// <summary>⭐ <c>CE-2275</c> D-2l — 1 while he fights from a frame point · 1 once the frame failed him this run (DA-84: the sill
        /// shape from then on) · 1 while the lean is still coming out (<see cref="PeekAndFireNodes.LeanSeconds"/>, since <see cref="LeanAt"/>).</summary>
        public byte Framed, FrameOff, Leaning;
        public double LeanAt;
        /// <summary>⭐ <c>CE-2328</c> — when the current blind burst began (sim s): it runs its count, for up to <c>ExposeSeconds</c> from HERE.</summary>
        public double BlindAt;
        /// <summary>⭐ <c>CE-2327</c> (R-2064) — the step-out spot's fire mark (<see cref="CheapLineOfSightTest.FireClearBit"/>): 0 = not marked,
        /// 1 = a round clears from it, 2 = it only SEES (he looks from it, and steps on to a marked spot before he fires).</summary>
        public byte PeekFire;
        /// <summary>⭐ <c>CE-2330</c> (R-2064) — 1 once the muscle refused a round from this hide point at the peek stance: he LOOKS at the
        /// peek stance and SHOOTS one stance taller from here (a low wall, a window sill). Reset with the hide point.</summary>
        public byte ShootTaller;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> P-6 (D7 + D8, B1–B8; R-234, R-238) — <c>PeekAndFire</c>: hide, expose briefly, fire, hide again, and move
    /// on when a position is used up. ONE machine flies both soldiers of the window duel — a stance peek (A: prone under the sill,
    /// kneel at it) and a step peek (B: behind a corner, step out) — only the parameters differ. 📄 docs/DESIGN_Peek_And_Fire.md §8.
    /// <para>Reuses the shared steps (R-174): the threat ranking and heard-contact aim (<see cref="EqsTacticsNodes.TopAim"/>), the
    /// sensor keeping (<see cref="PostureNodes.Keep"/>), the ONE aimed fire step (<c>PostureNodes.Fire</c> with B7's round
    /// count — the executor's sight gate and aim time apply), the blind burst (<see cref="FireAtPointNodes.FireAtPoint"/>), the
    /// stance request and the move.</para>
    /// </summary>
    public static class PeekAndFireNodes
    {
        /// <summary>The two sensor sites (with the run's owner stamp they find this run's sensors again).</summary>
        public const int CoverSite = 0x31360001, PeekSite = 0x31360002;

        /// <summary>Arrival radius of the node's moves (m) · a stance change's blend time (s).</summary>
        public const float ArrivalRadius = 0.5f, StanceBlendSeconds = 0.6f;

        /// <summary>⭐ <c>CE-3158</c> G5 — a candidate this close (3-D, m) to a friend's live <see cref="CoverClaim"/> is his · a claim
        /// lapses this long (s) after the node last refreshed it.</summary>
        public const float ClaimRadius = 1.5f, ClaimTtlSeconds = 2f;

        /// <summary>The §8.4 default column.</summary>
        public static readonly PeekAndFireParams Defaults = new()
        {
            HideTemplate = FindCoverFromTarget.BlueprintId,
            SearchRadius = 15f, PeekSearchRadius = 3f,
            HideSecondsMin = 2f, HideSecondsMax = 5f,
            ExposeSeconds = 3f, GraceSeconds = 0.6f, SuppressedSeconds = 3f,
            RoundsPerExposure = 3, BlindRounds = 3, FireCooldownSeconds = 0.3f, BlindAimHeight = 1.0f,
            ExposuresPerPosition = 3,
            HeatPerExposure = 1.0f, HeatWhenFiredUpon = 1.5f,
            BurnHeat = 2.5f, ReuseHeat = 1.0f, HeatPenalty = 0.3f,
            CoolHalfLifeSeconds = 45f, MatchRadius = 1.0f,
            RelocateSpeed = 4.5f, MinRelocateMetres = 4f,
            ScoreDeltaThreshold = 0.05f,
            NoCoverSeconds = 3f,
            HeardMatchRadius = DefaultHeardMatchRadius,
        };

        /// <summary>The parameters with every zero replaced by its default (the enums, flags and filter stay as authored).</summary>
        public static PeekAndFireParams Effective(in PeekAndFireParams a)
        {
            var d = Defaults;
            var e = a;
            if (e.HideTemplate == 0) e.HideTemplate = d.HideTemplate;
            if (e.SearchRadius <= 0f) e.SearchRadius = d.SearchRadius;
            if (e.PeekSearchRadius <= 0f) e.PeekSearchRadius = d.PeekSearchRadius;
            if (e.HideSecondsMin <= 0f) e.HideSecondsMin = d.HideSecondsMin;
            if (e.HideSecondsMax < e.HideSecondsMin) e.HideSecondsMax = Math.Max(e.HideSecondsMin, d.HideSecondsMax);
            if (e.ExposeSeconds <= 0f) e.ExposeSeconds = d.ExposeSeconds;
            if (e.GraceSeconds <= 0f) e.GraceSeconds = d.GraceSeconds;
            if (e.SuppressedSeconds <= 0f) e.SuppressedSeconds = d.SuppressedSeconds;
            if (e.RoundsPerExposure <= 0) e.RoundsPerExposure = d.RoundsPerExposure;
            if (e.BlindRounds <= 0) e.BlindRounds = d.BlindRounds;
            if (e.FireCooldownSeconds <= 0f) e.FireCooldownSeconds = d.FireCooldownSeconds;
            if (e.BlindAimHeight <= 0f) e.BlindAimHeight = d.BlindAimHeight;
            if (e.ExposuresPerPosition <= 0) e.ExposuresPerPosition = d.ExposuresPerPosition;
            if (e.HeatPerExposure <= 0f) e.HeatPerExposure = d.HeatPerExposure;
            if (e.HeatWhenFiredUpon <= 0f) e.HeatWhenFiredUpon = d.HeatWhenFiredUpon;
            if (e.BurnHeat <= 0f) e.BurnHeat = d.BurnHeat;
            if (e.ReuseHeat <= 0f) e.ReuseHeat = d.ReuseHeat;
            if (e.HeatPenalty <= 0f) e.HeatPenalty = d.HeatPenalty;
            if (e.CoolHalfLifeSeconds <= 0f) e.CoolHalfLifeSeconds = d.CoolHalfLifeSeconds;
            if (e.MatchRadius <= 0f) e.MatchRadius = d.MatchRadius;
            if (e.RelocateSpeed <= 0f) e.RelocateSpeed = d.RelocateSpeed;
            if (e.MinRelocateMetres <= 0f) e.MinRelocateMetres = d.MinRelocateMetres;
            if (e.ScoreDeltaThreshold <= 0f) e.ScoreDeltaThreshold = d.ScoreDeltaThreshold;
            if (e.NoCoverSeconds <= 0f) e.NoCoverSeconds = d.NoCoverSeconds;
            if (e.HeardMatchRadius <= 0f) e.HeardMatchRadius = d.HeardMatchRadius;
            return e;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2274</c> DA-C (DA-2, PC-15; 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8b) — the cycle's
        /// timings by the man's proficiency (<see cref="ProficiencyOf"/>, the one reader): exposure × <c>ExposeFactor</c>; bursts — Militia
        /// doubles them, Untrained empties the magazine; who switches — Untrained / Militia never leave a position by count (they stay at
        /// the sill), Veteran one exposure sooner, Elite after one. ⭐ EXACTLY the given values at Regular and with no component.
        /// </summary>
        public static PeekAndFireParams Skilled(in PeekAndFireParams p, EntityRepository world, Entity self)
        {
            var s = p;
            s.ExposeSeconds = p.ExposeSeconds * ProficiencyOf.ExposeFactor(world, self);
            switch (ProficiencyOf.Bursts(world, self))
            {
                case BurstDiscipline.LongBursts:
                    s.RoundsPerExposure = p.RoundsPerExposure * 2;
                    s.BlindRounds = p.BlindRounds * 2;
                    break;
                case BurstDiscipline.EmptyTheMagazine:
                    int magazine = MagazineOf(world, self);
                    s.RoundsPerExposure = Math.Max(p.RoundsPerExposure, magazine);
                    s.BlindRounds = Math.Max(p.BlindRounds, magazine);
                    break;
            }
            s.ExposuresPerPosition = ProficiencyOf.Tier(world, self) switch
            {
                ProficiencyTier.Untrained or ProficiencyTier.Militia => int.MaxValue,   // PC-15: stays at the sill (heat still burns a spot)
                ProficiencyTier.Veteran => Math.Max(1, p.ExposuresPerPosition - 1),
                ProficiencyTier.Elite => 1,
                _ => p.ExposuresPerPosition,
            };
            return s;
        }

        /// <summary>The rounds left in his magazine (its size when empty, 30 with no magazine) — what "empties the magazine" fires.</summary>
        private static int MagazineOf(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<WeaponState>(self)) return 30;
            ref readonly var w = ref world.GetComponentRO<WeaponState>(self);
            return w.MagazineSize <= 0 ? 30 : w.MagazineRounds > 0 ? w.MagazineRounds : w.MagazineSize;
        }

        /// <summary>B2–B5's numbers out of the node's parameters.</summary>
        public static HeatRules Rules(in PeekAndFireParams p) => new()
        {
            CoolHalfLifeSeconds = p.CoolHalfLifeSeconds, BurnHeat = p.BurnHeat, ReuseHeat = p.ReuseHeat,
            HeatPenalty = (p.Disable & PeekDisable.HeatPenalty) != 0 ? 0f : p.HeatPenalty,   // ⭐ CE-3158 G8
            MatchRadius = p.MatchRadius,
        };

        /// <summary>⭐ <c>CE-3158</c> G8 — an aimed exposure lasts at least twice the weapon's aim time (else it ends before a round
        /// can leave); <c>ExposeSeconds</c> otherwise.</summary>
        internal static float ExposeFor(in PeekAndFireParams p, EntityRepository world, Entity self)
            => MathF.Max(p.ExposeSeconds, 2f * AimAndFireExecutor.AimSecondsFor(world, self, 0));

        /// <summary>
        /// Runs the cycle (§8.2) against the unit's top threat. Running while it has one; Success when nothing is remembered;
        /// Failure when the unit cannot move or fire.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus PeekAndFire(ref PeekAndFireParams authored, ref PeekAndFireState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<LocomotionChannel>(self) || !world.HasComponent<WeaponChannel>(self)) return NodeStatus.Failure;
            var p = Skilled(Effective(in authored), world, self);   // ⭐ CE-2274 DA-C — the timings by his proficiency
            double now = Now(world);

            var was = new ThreatAim(ws.Threat, ws.HeardId, ws.HeardPoint);
            if (!EqsTacticsNodes.TopAim(world, self, in was, out var threat))
            {
                Release(ref ws, self, world);
                return NodeStatus.Success;   // nothing left to fight
            }
            KeepSensors(ref ws, in p, self, world, in was, in threat);

            ref var mem = ref UnitMemory.Ref<FiringPositionMemory>(world, self);
            switch (ws.Phase)
            {
                case PeekPhase.Choose:
                    if (ChooseFrame(ref ws, in p, ref mem, self, world, now, in threat)) break;   // ⭐ CE-2275 D-2l — beside the frame
                    // ⭐ CE-3158 G2 — no cover for NoCoverSeconds: say so, the posture picks something else
                    if (!ChooseOrWait(ref ws, in p, ref mem, self, world, now, in threat))
                    {
                        Release(ref ws, self, world);
                        return NodeStatus.Failure;
                    }
                    break;
                case PeekPhase.MoveToHide: MoveToHide(ref ws, in p, ref mem, self, world, now); break;
                case PeekPhase.Hidden:     Hidden(ref ws, in p, ref mem, self, world, now, in threat); break;
                case PeekPhase.Expose:     Expose(ref ws, in p, ref mem, self, world, now, in threat); break;
                case PeekPhase.Aimed:      Aimed(ref ws, in p, ref mem, self, world, now, in threat); break;
                case PeekPhase.Blind:      Blind(ref ws, in p, ref mem, self, world, now); break;
                case PeekPhase.Recover:    RecoverMove(ref ws, in p, self, world, now); break;
            }

            Claim(ref ws, self, world, now);   // ⭐ CE-3158 G5 — keep my claim live while I hold a point
            mem.Phase = (byte)ws.Phase;
            mem.PhaseAt = now;
            mem.PhaseUntil = ws.PhaseUntil;
            mem.HidePoint = ws.HidePoint;
            mem.PeekPoint = ws.PeekPoint;
            return NodeStatus.Running;
        }

        /// <summary>Leaving the node: its sensors go, its fire and its move stop. ⭐ The position memory stays (B1).</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.PeekAndFireNodes.PeekAndFire")]
        public static void Deactivate_PeekAndFire(ref PeekAndFireParams p, ref PeekAndFireState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world);

        // ── the phases ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <c>CE-3158</c> G2 (📄 docs/DESIGN_Peek_And_Fire.md §10.5) — the Choose phase with its honest outcomes: a usable point ⇒ go;
        /// answered points but every one burned ⇒ AIMED FIRE FROM WHERE IT STANDS while they cool (never silent); no answer at all
        /// for <c>NoCoverSeconds</c> ⇒ false (the node answers Failure).
        /// </summary>
        private static bool ChooseOrWait(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                         EntityRepository world, double now, in ThreatAim threat)
        {
            if (PickHide(ref ws, in p, ref mem, world, now, relocating: false, out var point, out var stance, out byte kind, self))
            {
                if (ws.Waiting == 2) PostureNodes.StopFiring(world, self, ref ws.Fire);
                ws.Waiting = 0;
                GoToHide(ref ws, in p, self, world, point, stance, kind);
                return true;
            }
            if (Answered(in ws, world))
            {
                // every point burned (or claimed): fight from here — the aimed step, only at a SEEN enemy (D4)
                ws.Waiting = 2;
                var target = LockTarget(world, self, in threat);
                if (!target.IsNull && SightNow.Sees(world, self, target))
                    PostureNodes.Fire(world, self, ref ws.Fire, p.FireCooldownSeconds, 0, target);
                else PostureNodes.StopFiring(world, self, ref ws.Fire);
                return true;
            }
            if (ws.Waiting != 1) { ws.Waiting = 1; ws.WaitingSince = now; }
            return now - ws.WaitingSince < p.NoCoverSeconds;
        }

        /// <summary>The cover sensor has answered with at least one point.</summary>
        private static bool Answered(in PeekAndFireState ws, EntityRepository world)
        {
            if (!ws.CoverSensor.IsValid || !world.IsAlive(ws.CoverSensor.ChildId)
                || !world.HasComponent<EqsCognitiveBuffer>(ws.CoverSensor.ChildId)) return false;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(ws.CoverSensor.ChildId);
            return buffer.IsReady && buffer.Count > 0;
        }

        private static void Choose(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                   EntityRepository world, double now, bool relocating)
        {
            if (!PickHide(ref ws, in p, ref mem, world, now, relocating, out var point, out var stance, out byte kind, self)) return;   // no answer yet
            GoToHide(ref ws, in p, self, world, point, stance, kind);
        }

        /// <summary>Takes <paramref name="point"/> as the hide point (its stances by the peek mode) and walks there.</summary>
        private static void GoToHide(ref PeekAndFireState ws, in PeekAndFireParams p, Entity self, EntityRepository world,
                                     Vector3 point, StanceId? stance, byte kind)
        {
            ws.PeekIsStep = (byte)(ChoosePeek(in p, stance, kind, StanceRequest.CanCarry(world, self), out var hide, out var peek) ? 1 : 0);
            ws.HideStance = hide;
            ws.PeekStance = peek;
            ws.Waiting = 0;
            ws.HidePoint = point;
            ws.PeekPoint = point;
            ws.ShootTaller = 0;   // ⭐ CE-2330
            ws.ExposuresHere = 0;
            ws.Bounding = 0;
            // ⭐ AQ96 D (CE-2320) — a move to a hide is a threat move by definition: crouched, at least the rush speed
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, point, MathF.Max(p.RelocateSpeed, UnderFire.RushSpeed), ArrivalRadius, MoveGait.Crouched) ? 1 : 0);
            ws.Phase = PeekPhase.MoveToHide;
        }

        /// <summary>
        /// ⭐ <c>CE-3158</c> G1 (📄 docs/DESIGN_Peek_And_Fire.md §10.5) — HOW THE UNIT EXPOSES, from the point it hides at: true = a
        /// step peek (hide at <paramref name="hide"/>, the peek stance is chosen at the peek point), false = a stance peek (hide at
        /// <paramref name="hide"/>, come up to <paramref name="peek"/> on the same spot). <paramref name="kind"/> is
        /// <see cref="EqsResult.Kind"/>'s encoding; <paramref name="canStance"/> = the unit carries a stance at all.
        /// </summary>
        internal static bool ChoosePeek(in PeekAndFireParams p, StanceId? stance, byte kind, bool canStance, out StanceId hide, out StanceId peek)
        {
            StanceId? hideOverride = p.HideStanceOverride != 0 ? (StanceId)(p.HideStanceOverride - 1) : null;
            StanceId? peekOverride = p.PeekStanceOverride != 0 ? (StanceId)(p.PeekStanceOverride - 1) : null;
            var mode = p.Mode;
            if (mode == PeekMode.Auto)
            {
                var r = new EqsResult { Kind = kind };
                if (!canStance || !r.TryGetKind(out var k)) mode = PeekMode.Step;   // no stance, or not a cover point: step out
                else if (k == CoverKind.WindowFiring) mode = PeekMode.Stance;       // a window: up at it, at its stance
                else
                {
                    // a cover: hide at ITS stance, come up one taller — a cover already standing has nothing taller ⇒ step out
                    hide = hideOverride ?? stance ?? StanceId.Crouched;
                    if (peekOverride is { } po ? Taller(po, hide) : Taller(StanceId.Standing, hide))
                    {
                        peek = peekOverride ?? OneTaller(hide);
                        return false;
                    }
                    mode = PeekMode.Step;
                }
            }
            if (mode == PeekMode.Step)
            {
                hide = hideOverride ?? stance ?? StanceId.Crouched;
                peek = StanceId.Standing;   // chosen at the peek point
                return true;
            }
            peek = peekOverride ?? stance ?? StanceId.Crouched;   // the window's stance (P-1: crouched at a sill)
            hide = hideOverride ?? StanceId.Prone;
            return false;
        }

        /// <summary>True when <paramref name="a"/> is a taller stance than <paramref name="b"/> (Standing &gt; Crouched &gt; Prone).</summary>
        private static bool Taller(StanceId a, StanceId b) => Height(a) > Height(b);

        private static int Height(StanceId s) => s switch { StanceId.Prone => 0, StanceId.Crouched => 1, _ => 2 };

        private static StanceId OneTaller(StanceId s) => s == StanceId.Prone ? StanceId.Crouched : StanceId.Standing;

        private static void MoveToHide(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                       EntityRepository world, double now)
        {
            var status = LocomotionMoveTo.Status(world, self);
            if (status == NodeStatus.Running) return;
            ws.Moving = 0;
            if (status == NodeStatus.Success) { EnterHidden(ref ws, in p, self, world, now); return; }
            if (ws.Framed == 1)
            {
                // ⭐ CE-2275 D-2l (DA-84) — he cannot reach the frame: he fights this post the sill way from now on
                ws.Framed = 0;
                ws.FrameOff = 1;
                ws.Phase = PeekPhase.Choose;
                return;
            }
            // ⭐ CE-3158 G2 — the move failed: the point is (maybe) unreachable — heat it by UnreachableHeat × BurnHeat, so a second
            //   failure burns it and it is never picked again while it stays hot (one failure may be a path not ready yet)
            Unreachable(ref mem, ws.HidePoint, in p, now);
            ws.Phase = PeekPhase.Choose;
        }

        /// <summary>⭐ <c>CE-3158</c> G2 — the share of <c>BurnHeat</c> one failed move adds: two failures burn the point.</summary>
        public const float UnreachableHeat = 0.6f;

        private static void Unreachable(ref FiringPositionMemory mem, Vector3 point, in PeekAndFireParams p, double now)
            => PositionHeat.Add(ref mem, point, UnreachableHeat * p.BurnHeat, now, Rules(in p), exposure: false);

        private static void Hidden(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                   EntityRepository world, double now, in ThreatAim threat)
        {
            if (ws.Framed == 1) { FrameHidden(ref ws, in p, ref mem, self, world, now); return; }   // ⭐ CE-2275 D-2l
            StanceRequest.Set(world, self, ws.HideStance, StanceBlendSeconds);

            // B4/D8 — a used-up position: move on if there is somewhere else (a burned one with nowhere to go waits to cool)
            bool burned = PositionHeat.IsBurnedAt(ref mem, ws.HidePoint, now, Rules(in p));
            if (ws.Bounding == 0 && (burned || ws.ExposuresHere >= p.ExposuresPerPosition))
            {
                if (p.SuppressBeforeRelocate != 0 && (p.Disable & PeekDisable.SuppressAndBound) == 0)
                {
                    // ⭐ P-7 D12 — SUPPRESS AND BOUND: pick the next cover now, but first come up and fire a suppressive burst at the
                    //   freshest evidence (below, through the exposure); the run to the new cover starts when the burst ends (Recover).
                    if (PickHide(ref ws, in p, ref mem, world, now, relocating: true, out var next, out var nextStance, out byte nextKind, self))
                    {
                        ws.Bounding = 1;
                        ws.NextHide = next;
                        ws.NextStance = nextStance is { } ns ? (byte)((byte)ns + 1) : (byte)0;
                        ws.NextKind = nextKind;
                        ws.PhaseUntil = now;   // no further wait: the bound is the reason to come up
                    }
                    else if (burned) return;   // nowhere to go: a burned spot waits to cool
                }
                else
                {
                    Choose(ref ws, in p, ref mem, self, world, now, relocating: true);   // nothing elsewhere ⇒ changes nothing
                    if (ws.Phase == PeekPhase.MoveToHide || burned) return;
                }
            }

            // B8 — reloading or under fire: stay down (the dispatcher runs the reload; an empty magazine starts one here)
            bool reloading = ReloadingOrEmpty(world, self);
            // ⭐ CE-3158 G3 — the ROE decides whether to come up at all: HoldFire never exposes (hide only); ReturnFire exposes
            //   only inside the return-fire window, and then WITHOUT the random wait (the window is the reason to come up)
            if (!AimAndFireExecutor.RoePermitsFire(world, self)) return;
            bool returning = world.IsComponentTypeRegistered<Roe>() && RoeOf.Fire(world, self) == RoeFire.ReturnFire;
            if (reloading || Suppressed(world, self, in p) || (!returning && now < ws.PhaseUntil)) return;

            if (ws.PeekIsStep == 0)
            {
                ws.PeekPoint = ws.HidePoint;
                StanceRequest.Set(world, self, ws.PeekStance, StanceBlendSeconds);
                BeginExposure(ref ws, in p, ref mem, now);
                ws.Phase = PeekPhase.Expose;
                return;
            }

            if (!PickPeek(ref ws, in p, ref mem, world, now, out var peek, out var peekStance, self))
            {
                // ⭐ CE-3158 G2 — no step-out point for NoCoverSeconds: this cover cannot be fought from — to the next one
                if (ws.Waiting != 1) { ws.Waiting = 1; ws.WaitingSince = now; }
                else if (now - ws.WaitingSince >= p.NoCoverSeconds)
                {
                    PositionHeat.Add(ref mem, ws.HidePoint, p.BurnHeat, now, Rules(in p), exposure: false);
                    ws.Waiting = 0;
                    ws.Phase = PeekPhase.Choose;
                }
                return;
            }
            ws.Waiting = 0;
            ws.PeekPoint = peek;
            ws.PeekStance = peekStance ?? StanceId.Standing;
            ws.Stepping = 1;
            ws.Counted = 0;
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, peek, p.RelocateSpeed, ArrivalRadius, MoveGait.Keep) ? 1 : 0);   // AQ88: the side-step keeps this node's own stance
            ws.Phase = PeekPhase.Expose;
        }

        private static void Expose(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                   EntityRepository world, double now, in ThreatAim threat)
        {
            if (ws.Leaning == 1)
            {
                // ⭐ CE-2275 D-2l (DA-83) — the lean is instant in the model; the exposure starts once it would have come out
                if (now - ws.LeanAt < LeanSeconds) return;
                ws.Leaning = 0;
                BeginExposure(ref ws, in p, ref mem, now);
            }
            if (ws.Stepping == 1)
            {
                var status = LocomotionMoveTo.Status(world, self);
                if (status == NodeStatus.Running) return;
                ws.Stepping = 0;
                ws.Moving = 0;
                if (status != NodeStatus.Success) { Recover(ref ws, in p, ref mem, self, world, now, firedUpon: false); return; }
                StanceRequest.Set(world, self, ws.PeekStance, StanceBlendSeconds);
                BeginExposure(ref ws, in p, ref mem, now);
            }

            if (ws.Bounding == 1)
            {
                // ⭐ P-7 D12 — the suppressive burst: no aim time, no grace — at the freshest evidence, seen or not
                if (!StartBurst(ref ws, in p, self, world, in threat)) Recover(ref ws, in p, ref mem, self, world, now, false);
                return;
            }

            var target = LockTarget(world, self, in threat);
            if (!target.IsNull && SightNow.Sees(world, self, target))
            {
                ws.LastSeenAt = now;
                // ⭐ CE-2274 DA-C — a NEW target: below Regular he takes his reaction delay before the first shot, staying up meanwhile
                float delay = ProficiencyOf.ReactionDelaySeconds(world, self);
                if (delay > 0f && !target.Equals(ws.Engaged))
                {
                    if (!target.Equals(ws.Reacting)) { ws.Reacting = target; ws.ReactingSince = now; }
                    if (now - ws.ReactingSince < delay) return;
                }
                if (StepToShoot(ref ws, in p, ref mem, self, world, now)) return;   // ⭐ CE-2327 — seen from a look-only spot
                ws.Engaged = target;
                ws.Fire = default;   // a fresh aimed action each exposure (B7's count starts at 0)
                // ⭐ CE-3158 G4 — the target is LOCKED for the exposure: the one whose sight was just checked is the one shot at
                PostureNodes.Fire(world, self, ref ws.Fire, p.FireCooldownSeconds, p.RoundsPerExposure, target);
                ws.Phase = PeekPhase.Aimed;
                return;
            }
            if (now - ws.ExposedAt < p.GraceSeconds) return;
            if ((p.Disable & PeekDisable.BlindFire) == 0 && StepToShoot(ref ws, in p, ref mem, self, world, now)) return;   // ⭐ CE-2327

            // 4.2 — not seen in time: a blind burst at the FRESHEST evidence (D13), raised to the body's middle (no aim time);
            //   ⭐ CE-3158 G8 — or, with blind fire switched off, straight back down
            if ((p.Disable & PeekDisable.BlindFire) != 0 || !StartBurst(ref ws, in p, self, world, in threat))
                Recover(ref ws, in p, ref mem, self, world, now, false);
        }

        /// <summary>
        /// ⭐ <c>CE-3158</c> G4 — the target of this exposure: the squad's ASSIGNED target (<see cref="SquadAssignment"/>) when the unit has
        /// one that is alive and seen, else the top threat's entity; <see cref="Entity.Null"/> when the threat is only a heard point.
        /// </summary>
        internal static Entity LockTarget(EntityRepository world, Entity self, in ThreatAim threat)
        {
            if (SquadAssignment.TargetOf(world, self, out long handle))
            {
                var assigned = new Entity((ulong)handle);
                if (world.IsAlive(assigned) && SightNow.Sees(world, self, assigned)
                    && Fdp.Toolkit.Behavior.Components.EngagementAreas.Allows(world, self, assigned)) return assigned;   // ⭐ CE-2282
            }
            if (threat.IsPoint) return Entity.Null;
            // ⭐ CE-2282 DA-27 — the threat he hides from may lie outside his sector: then he shoots the first one inside it, if any
            return Fdp.Toolkit.Behavior.Components.EngagementAreas.Allows(world, self, threat.Entity) ? threat.Entity : EqsTacticsNodes.FirstAllowedRemembered(world, self);
        }

        /// <summary>A burst of <c>BlindRounds</c> at the freshest evidence (D13) + <c>BlindAimHeight</c>; false = nowhere known.</summary>
        private static bool StartBurst(ref PeekAndFireState ws, in PeekAndFireParams p, Entity self, EntityRepository world, in ThreatAim threat)
        {
            if (!FreshestEvidence(world, self, in threat, out var at, out long slotId, p.HeardMatchRadius)) return false;
            // ⭐ CE-2326 (R-3030; Peek and Fire §11.6a) — a body last SEEN there: the round names it (his feet + the stance he was seen in)
            //   and the muscle aims it — his chest, or the band that showed over a cover. Heard only (no stance): the spot + the height.
            byte stance = SeenStanceOf(world, self, slotId);
            ws.Blind = new FireAtPointNodeParams
            {
                Point = stance != 0 ? at : at + new Vector3(0f, 0f, p.BlindAimHeight), BodyStance = stance,
                CooldownSeconds = p.FireCooldownSeconds, Rounds = p.BlindRounds,
            };
            FireAtPointNodes.FireAtPoint(ref ws.Blind, self, world);
            ws.Phase = PeekPhase.Blind;
            ws.BlindAt = Now(world);   // ⭐ CE-2328
            return true;
        }

        /// <summary>⭐ <c>CE-2326</c> — the stance the memory slot <paramref name="slotId"/> was last SEEN in, as <c>StanceId + 1</c>; 0 =
        /// unknown (heard only, never seen, no memory).</summary>
        private static unsafe byte SeenStanceOf(EntityRepository world, Entity self, long slotId)
        {
            if (!world.HasComponent<TargetMemory>(self)) return 0;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == slotId)
                    return TargetMemory.StanceSeen(in mem, i) is { } s ? (byte)(s + 1) : (byte)0;
            return 0;
        }

        /// <summary>
        /// ⭐ P-7 D13 — THE FRESHEST EVIDENCE WINS for a burst: the newest (by <see cref="TargetMemory.LastSeenTick"/>) of the threat's
        /// own slot and every HEARD (anonymous) contact. 📐 A shot heard more than ≈ 6 m from the remembered spot makes a NEW anonymous
        /// slot (<c>TargetMemory.HearContact</c>) and the ranking prefers the identified one — without this the burst goes where the
        /// enemy WAS. Ties keep the threat's own slot. ⚠ An aimed shot still needs sight (D4); this is only where a burst goes.
        /// </summary>
        internal static bool FreshestEvidence(EntityRepository world, Entity self, in ThreatAim threat, out Vector3 at)
            => FreshestEvidence(world, self, in threat, out at, out _);

        /// <summary>As above, and <paramref name="slotId"/> = the memory slot the evidence came from (the threat's own id when no
        /// heard slot is newer).</summary>
        internal static unsafe bool FreshestEvidence(EntityRepository world, Entity self, in ThreatAim threat, out Vector3 at, out long slotId,
                                                     float matchRadius = 0f)
        {
            bool have = EqsTacticsNodes.ThreatPosition(world, self, in threat, out at);
            // ⭐ CE-3158 G4 — with a radius, a heard slot counts only near where THIS threat is remembered (another enemy's shot does
            //   not drag the burst away from the locked target); a heard-only threat has no spot of its own to measure from
            var origin = at;
            bool filter = matchRadius > 0f && have && !threat.IsPoint;
            long id = threat.IsPoint ? threat.HeardId : (long)threat.Entity.PackedValue;
            slotId = id;
            if (!world.HasComponent<TargetMemory>(self)) return have;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            uint newest = 0;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == id) { newest = mem.LastSeenTick[i]; break; }
            for (int i = 0; i < mem.Count; i++)
            {
                if (!TargetMemory.IsAnonymous(in mem, i) || TargetMemory.IsBurst(in mem, i) || mem.LastSeenTick[i] <= newest) continue;   // R-2071 — no shooter stands where his round landed
                var heard = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]);
                if (filter && Vector3.Distance(heard, origin) > matchRadius) continue;
                newest = mem.LastSeenTick[i];
                at = heard;
                slotId = mem.EntityIds[i];
                have = true;
            }
            return have;
        }

        private static void Aimed(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                  EntityRepository world, double now, in ThreatAim threat)
        {
            if (!ws.Fire.Threat.IsNull && SightNow.Sees(world, self, ws.Fire.Threat)) ws.LastSeenAt = now;   // the LOCKED target
            ref readonly var channel = ref world.GetComponentRO<WeaponChannel>(self);
            bool done = channel.ActiveAction == CombatConstants.ActionIdAimAndFire && channel.Status != NodeStatus.Running;
            // ⭐ CE-2228 (Exposure Cycle §7, V6: "a TOW-type gunner holds sight through the flight") — a command-guided round in flight
            //   needs its gunner: neither the exposure cap nor a lost sight brings him down; only the round landing (done) or being shot at
            bool guiding = Guiding(world, self);
            // ⭐ CE-2218 — the weapon holds MASKED (its own round would stop in the wall in front of the muzzle — the eye's line passed the
            //   window, the barrel's does not): this spot cannot shoot that target. Burn it and come down; the next choice moves on.
            if (!guiding && MaskedSince(world, self, ws.ExposedAt))
            {
                if (ShootHigher(ref ws, in p, ref mem, self, world, now)) return;   // ⭐ CE-2330 — taller, or a spot a round clears from
                PositionHeat.Add(ref mem, ws.HidePoint, p.BurnHeat, now, Rules(in p), exposure: false);
                Recover(ref ws, in p, ref mem, self, world, now, false);
                return;
            }
            bool firedUpon = FiredUponSince(world, self, ws.ExposedAt);
            if (done || firedUpon || (!guiding && (now - ws.ExposedAt >= ExposeFor(in p, world, self) || now - ws.LastSeenAt > p.GraceSeconds)))
                Recover(ref ws, in p, ref mem, self, world, now, firedUpon);
        }

        private static void Blind(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                  EntityRepository world, double now)
        {
            var status = FireAtPointNodes.FireAtPoint(ref ws.Blind, self, world);
            // ⭐ CE-2330 — a burst refused as masked: taller / a clearing spot, else this spot is burned (as an aimed round's, CE-2218)
            if (MaskedSince(world, self, ws.BlindAt))
            {
                if (ShootHigher(ref ws, in p, ref mem, self, world, now)) return;
                PositionHeat.Add(ref mem, ws.HidePoint, p.BurnHeat, now, Rules(in p), exposure: false);
                Recover(ref ws, in p, ref mem, self, world, now, false);
                return;
            }
            bool firedUpon = FiredUponSince(world, self, ws.ExposedAt);
            // ⭐ CE-2328 — the burst's own time, from when IT began. ⛔ SUPERSEDED: from when he came up — the grace and the turn had
            //   already spent it, so he went down after ONE round and the rest of the burst was refused against his own sill (measured,
            //   bt-window-duel: every burst from the window cut to one round)
            if (status != NodeStatus.Running || firedUpon || now - ws.BlindAt >= p.ExposeSeconds)
                Recover(ref ws, in p, ref mem, self, world, now, firedUpon);
        }

        private static void RecoverMove(ref PeekAndFireState ws, in PeekAndFireParams p, Entity self, EntityRepository world, double now)
        {
            var status = LocomotionMoveTo.Status(world, self);
            if (status == NodeStatus.Running) return;
            ws.Moving = 0;
            if (status == NodeStatus.Success) EnterHidden(ref ws, in p, self, world, now);
            else ws.Phase = PeekPhase.Choose;
        }

        // ── ⭐ CE-2275 D-2l — the frame (DA-77…DA-84, R-2059; 📄 DESIGN_Defend_And_Attack_Position.md §8c D-2l) ──────────────────

        /// <summary>DA-83 — how long the lean takes before the exposure counts and the first round may leave (s).</summary>
        public const float LeanSeconds = 0.4f;

        /// <summary>A frame post that has not failed him: Choose takes the frame point AWAY from the target's bearing (DA-79) — the other
        /// one when that is burned — and walks there. False = no frame (none, failed, both burned, or no spot known): the cycle as before.</summary>
        private static bool ChooseFrame(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                        EntityRepository world, double now, in ThreatAim threat)
        {
            if (p.Frame == 0 || ws.FrameOff == 1 || !EqsTacticsNodes.ThreatPosition(world, self, in threat, out var at)) return false;
            var point = WindowFrame.Facing(p.FrameA, p.FrameB, p.Opening, at);
            var rules = Rules(in p);
            if (PositionHeat.IsBurnedAt(ref mem, point, now, rules)) point = WindowFrame.Other(p.FrameA, p.FrameB, point);
            if (PositionHeat.IsBurnedAt(ref mem, point, now, rules)) return false;
            if (ws.Waiting == 2) PostureNodes.StopFiring(world, self, ref ws.Fire);
            GoToFrame(ref ws, in p, self, world, point);
            return true;
        }

        private static void GoToFrame(ref PeekAndFireState ws, in PeekAndFireParams p, Entity self, EntityRepository world, Vector3 point)
        {
            Leans.Set(world, self, LeanSide.LeanNone);
            ws.Framed = 1;
            ws.PeekIsStep = 0;
            ws.HideStance = ws.PeekStance = StanceId.Standing;   // DA-80 — upright beside the frame, hidden by the wall
            ws.Waiting = 0;
            ws.HidePoint = ws.PeekPoint = point;
            ws.ShootTaller = 0;   // ⭐ CE-2330
            ws.ExposuresHere = 0;
            ws.Bounding = 0;
            // ⭐ AQ96 D (CE-2320) — a move to a hide is a threat move by definition: crouched, at least the rush speed
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, point, MathF.Max(p.RelocateSpeed, UnderFire.RushSpeed), ArrivalRadius, MoveGait.Crouched) ? 1 : 0);
            ws.Phase = PeekPhase.MoveToHide;
        }

        /// <summary>
        /// Hidden at a frame point (DA-80): standing behind the wall — prone while suppressed; a used-up side (burned — a masked shot
        /// burns it — or its exposures spent) ⇒ he crosses to the other frame point (DA-79); under <see cref="RoeFire.HoldFire"/> he leans
        /// out to WATCH and never fires (DA-6's observation); else, when the wait is over, he leans out (<see cref="LeanSeconds"/>).
        /// </summary>
        private static void FrameHidden(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                        EntityRepository world, double now)
        {
            bool suppressed = Suppressed(world, self, in p);
            StanceRequest.Set(world, self, suppressed ? StanceId.Prone : StanceId.Standing, StanceBlendSeconds);
            var rules = Rules(in p);
            bool burned = PositionHeat.IsBurnedAt(ref mem, ws.HidePoint, now, rules);
            if (burned || ws.ExposuresHere >= p.ExposuresPerPosition)
            {
                var other = WindowFrame.Other(p.FrameA, p.FrameB, ws.HidePoint);
                if (!PositionHeat.IsBurnedAt(ref mem, other, now, rules)) { GoToFrame(ref ws, in p, self, world, other); return; }
                if (burned) { Leans.Set(world, self, LeanSide.LeanNone); return; }   // both sides used up: he waits for one to cool
            }
            bool reloading = ReloadingOrEmpty(world, self);
            if (!AimAndFireExecutor.RoePermitsFire(world, self))
            {
                Leans.Set(world, self, suppressed ? LeanSide.LeanNone : LeanToward(world, self, in p, ws.HidePoint));
                return;
            }
            bool returning = world.IsComponentTypeRegistered<Roe>() && RoeOf.Fire(world, self) == RoeFire.ReturnFire;
            if (reloading || suppressed || (!returning && now < ws.PhaseUntil)) { Leans.Set(world, self, LeanSide.LeanNone); return; }
            Leans.Set(world, self, LeanToward(world, self, in p, ws.HidePoint));
            ws.PeekPoint = ws.HidePoint;
            ws.Leaning = 1;
            ws.LeanAt = now;
            ws.Phase = PeekPhase.Expose;
        }

        /// <summary>DA-79 (refined) — the side he leans toward the opening: by his body's facing, or by the frame's own geometry when he has
        /// no transform.</summary>
        internal static LeanSide LeanToward(EntityRepository world, Entity self, in PeekAndFireParams p, Vector3 at)
        {
            var side = WindowFrame.SideToward(world, self, p.Opening);
            return side != LeanSide.LeanNone ? side : WindowFrame.SideFromFrame(p.FrameA, p.FrameB, p.Opening, at);
        }

        // ── the steps ───────────────────────────────────────────────────────────────────────────────

        /// <summary>The exposure counts once it is real (up at the window, or arrived at the peek point): a use and B2's heat.</summary>
        private static void BeginExposure(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, double now)
        {
            ws.ExposedAt = now;
            ws.LastSeenAt = 0;   // not seen this exposure (a time before any exposure) — ⛔ never an infinity: the block is dumped as JSON
            if (ws.Counted == 1) return;
            ws.Counted = 1;
            ws.ExposuresHere++;
            ws.Exposures++;
            PositionHeat.Add(ref mem, ws.HidePoint, p.HeatPerExposure, now, Rules(in p), exposure: true);
        }

        /// <summary>The exposure ends: the fire stops, being shot at heats the position more (B2), and the unit goes back down.</summary>
        private static void Recover(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                    EntityRepository world, double now, bool firedUpon)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            StopBlind(ref ws, self, world);
            if (firedUpon) PositionHeat.Add(ref mem, ws.HidePoint, p.HeatWhenFiredUpon, now, Rules(in p), exposure: false);
            ws.Counted = 0;
            if (ws.Bounding == 1)
            {
                // ⭐ P-7 D12 — the burst is over: RUN to the next cover (never back to the used one)
                GoToHide(ref ws, in p, self, world, ws.NextHide, ws.NextStance != 0 ? (StanceId)(ws.NextStance - 1) : null, ws.NextKind);
                return;
            }
            if (ws.PeekIsStep == 1 && Vector3.Distance(ws.PeekPoint, ws.HidePoint) > ArrivalRadius)
            {
                StanceRequest.Set(world, self, ws.HideStance, StanceBlendSeconds);
                ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, ws.HidePoint, p.RelocateSpeed, ArrivalRadius, MoveGait.Keep) ? 1 : 0);   // AQ88: the step back keeps the hide stance just set
                ws.Phase = PeekPhase.Recover;
                return;
            }
            EnterHidden(ref ws, in p, self, world, now);
        }

        private static void EnterHidden(ref PeekAndFireState ws, in PeekAndFireParams p, Entity self, EntityRepository world, double now)
        {
            if (ws.Framed == 1) Leans.Set(world, self, LeanSide.LeanNone);   // ⭐ CE-2275 D-2l — straighten, behind the wall
            StanceRequest.Set(world, self, ws.HideStance, StanceBlendSeconds);
            var rng = SimRng.FromSim(self.Index, CoverSite ^ (ws.Exposures * 7919), (float)now);
            ws.PhaseUntil = now + p.HideSecondsMin + rng.NextSingle() * (p.HideSecondsMax - p.HideSecondsMin);
            ws.Phase = PeekPhase.Hidden;
        }

        /// <summary>B6 — the best hide point of the cover sensor's answer span by <c>score − heat penalty</c>: burned ones skipped,
        /// and when relocating none closer than <c>MinRelocateMetres</c> to the current one.</summary>
        private static unsafe bool PickHide(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem,
                                            EntityRepository world, double now, bool relocating, out Vector3 point, out StanceId? stance,
                                            out byte kind, Entity self = default)
        {
            point = default; stance = null; kind = 0;
            if (!ws.CoverSensor.IsValid || !world.IsAlive(ws.CoverSensor.ChildId)
                || !world.HasComponent<EqsCognitiveBuffer>(ws.CoverSensor.ChildId)) return false;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(ws.CoverSensor.ChildId);
            if (!buffer.IsReady || buffer.Count == 0) return false;

            var span = buffer.GetSpanRO()[..buffer.Count];   // ⭐ CE-3144 — the span is all 16 slots; past Count they are (0,0,0) score 0
            float best = float.NegativeInfinity;
            for (int i = 0; i < span.Length; i++)
            {
                var r = span[i];
                var at = new Vector3(r.PositionX, r.PositionY, r.PositionZ);
                if (relocating && Vector3.Distance(at, ws.HidePoint) < p.MinRelocateMetres) continue;
                if (!OnPost(in p, at)) continue;                         // ⭐ CE-2275 D-2a (PC-14) — never off his post
                if (ClaimedByAFriend(world, self, at, now)) continue;   // ⭐ CE-3158 G5 — first claim wins
                float score = r.Score - PositionHeat.Penalty(ref mem, at, now, Rules(in p));
                if (score <= best) continue;   // ties keep the first: the sensor's order, deterministic
                best = score;
                point = at;
                stance = r.TryGetStance(out var s) ? s : null;
                kind = r.Kind;
            }
            return !float.IsNegativeInfinity(best);
        }

        /// <summary>⭐ <c>CE-2275</c> D-2a (PC-14) — <paramref name="at"/> is one of his post's points (within <c>AnchorRadius</c> of it) or
        /// his alternate; true with no post.</summary>
        internal static bool OnPost(in PeekAndFireParams p, Vector3 at)
        {
            if (p.AnchorRadius <= 0f) return true;
            if (Vector3.Distance(at, p.Anchor) <= p.AnchorRadius) return true;
            return p.AnchorHasAlternate != 0 && Vector3.Distance(at, p.AnchorAlternate) <= p.MatchRadius;
        }

        /// <summary>The step-out point: the peek sensor's best answer within <c>PeekSearchRadius</c> of the hide point.</summary>
        private static bool PickPeek(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem,
                                     EntityRepository world, double now, out Vector3 point, out StanceId? stance, Entity self = default)
        {
            point = default; stance = null;
            if (!ws.PeekSensor.IsValid || !world.IsAlive(ws.PeekSensor.ChildId)
                || !world.HasComponent<EqsCognitiveBuffer>(ws.PeekSensor.ChildId)) return false;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(ws.PeekSensor.ChildId);
            if (!buffer.IsReady || buffer.Count == 0) return false;
            var span = buffer.GetSpanRO()[..buffer.Count];   // ⭐ CE-3144 — the span is all 16 slots; past Count they are (0,0,0) score 0
            for (int i = 0; i < span.Length; i++)
            {
                var r = span[i];
                var at = new Vector3(r.PositionX, r.PositionY, r.PositionZ);
                if (Vector3.Distance(at, ws.HidePoint) > p.PeekSearchRadius + ArrivalRadius) continue;
                if (ClaimedByAFriend(world, self, at, now)) continue;   // ⭐ CE-3158 G5
                point = at;
                stance = r.TryGetStance(out var s) ? s : null;
                ws.PeekFire = FireMark(in r);
                return true;   // the sensor's order is its ranking
            }
            return false;
        }

        /// <summary>⭐ <c>CE-2327</c> — an answer's fire mark: 0 not marked · 1 a round clears from it · 2 it only sees.</summary>
        private static byte FireMark(in EqsResult r) => CheapLineOfSightTest.FireMarkOf(in r);

        /// <summary>
        /// ⭐⭐ <c>CE-2327</c> (R-2064) — the SHOOTING spot: the step-peek sensor's marked answer (a round clears from it) nearest to where he
        /// looked from, within <c>PeekSearchRadius</c> of the hide point and not a friend's. 🔒 User: <i>"if attacking is desired, then the spot
        /// must let him shoot. But the soldier looks out first … for peeking the place does not need to allow shooting"</i>.
        /// </summary>
        private static bool PickShoot(ref PeekAndFireState ws, in PeekAndFireParams p, EntityRepository world, double now, Entity self,
                                      out Vector3 point, out StanceId? stance)
        {
            point = default; stance = null;
            if (!ws.PeekSensor.IsValid || !world.IsAlive(ws.PeekSensor.ChildId)
                || !world.HasComponent<EqsCognitiveBuffer>(ws.PeekSensor.ChildId)) return false;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(ws.PeekSensor.ChildId);
            if (!buffer.IsReady || buffer.Count == 0) return false;
            var span = buffer.GetSpanRO()[..buffer.Count];
            float best = float.MaxValue;
            for (int i = 0; i < span.Length; i++)
            {
                var r = span[i];
                if (FireMark(in r) != 1) continue;
                var at = new Vector3(r.PositionX, r.PositionY, r.PositionZ);
                if (Vector3.Distance(at, ws.HidePoint) > p.PeekSearchRadius + ArrivalRadius) continue;
                if (ClaimedByAFriend(world, self, at, now)) continue;
                float d = Vector3.Distance(at, ws.PeekPoint);
                if (d >= best) continue;
                best = d;
                point = at;
                stance = r.TryGetStance(out var s) ? s : null;
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// ⭐ <c>CE-2327</c> — before he FIRES from a spot that only sees: he steps on to the nearest spot a round clears from (true: the step is
        /// under way, or there is none and he goes back down having looked). False = fire from here.
        /// </summary>
        private static bool StepToShoot(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                        EntityRepository world, double now)
        {
            // ⭐ CE-2330 — a stance peek that learned it must shoot taller here: up one stance before the round (never a look stance higher)
            if (ws.PeekIsStep == 0)
            {
                if (ws.ShootTaller == 1 && ws.PeekStance != StanceId.Standing) StanceRequest.Set(world, self, Taller(ws.PeekStance), StanceBlendSeconds);
                return false;
            }
            if (ws.PeekFire != 2) return false;
            if (!PickShoot(ref ws, in p, world, now, self, out var spot, out var stance))
            {
                Recover(ref ws, in p, ref mem, self, world, now, firedUpon: false);   // nowhere to shoot from: he has looked, and goes down
                return true;
            }
            ws.PeekFire = 1;
            ws.PeekPoint = spot;
            if (stance is { } s) ws.PeekStance = s;
            ws.Stepping = 1;
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, spot, p.RelocateSpeed, ArrivalRadius, MoveGait.Keep) ? 1 : 0);
            return true;
        }

        /// <summary>One stance taller (prone → crouched → standing).</summary>
        private static StanceId Taller(StanceId stance) => stance == StanceId.Prone ? StanceId.Crouched : StanceId.Standing;

        /// <summary>
        /// ⭐⭐ <c>CE-2330</c> (R-2064) — the muscle REFUSED his round as masked (R-3018): before giving the spot up, shoot from where a round
        /// clears — a stance peek comes up ONE stance (and remembers it for this hide point, so the next exposure looks low and shoots
        /// taller); a step peek steps on to the nearest marked spot (<see cref="PickShoot"/>). True = handled (he stays up, or steps);
        /// false = nowhere better: the caller burns the spot (CE-2218). A frame post already crosses to its other point (D-2l).
        /// </summary>
        private static bool ShootHigher(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                        EntityRepository world, double now)
        {
            if (ws.Framed == 1) return false;
            if (ws.PeekIsStep == 0)
            {
                if (ws.ShootTaller == 1 || ws.PeekStance == StanceId.Standing || !StanceRequest.CanCarry(world, self)) return false;
                ws.ShootTaller = 1;
                StanceRequest.Set(world, self, Taller(ws.PeekStance), StanceBlendSeconds);
            }
            else
            {
                if (!PickShoot(ref ws, in p, world, now, self, out var spot, out var stance) || Vector3.Distance(spot, ws.PeekPoint) <= ArrivalRadius)
                    return false;
                PostureNodes.StopFiring(world, self, ref ws.Fire);
                StopBlind(ref ws, self, world);
                ws.PeekFire = 1;
                ws.PeekPoint = spot;
                if (stance is { } s) ws.PeekStance = s;
                ws.Stepping = 1;
                ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, spot, p.RelocateSpeed, ArrivalRadius, MoveGait.Keep) ? 1 : 0);
                ws.Phase = PeekPhase.Expose;
                return true;
            }
            // the same exposure goes on, taller: a fresh aimed action (the refused one is spent), its masked stamp now in the past
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            StopBlind(ref ws, self, world);
            ws.Fire = default;
            ws.ExposedAt = now + 1e-3;   // just after this tick: the refusal that brought him up is behind it
            ws.Phase = PeekPhase.Expose;
            return true;
        }

        /// <summary>The cover sensor always; the step-peek sensor once a step peek is chosen — both pointed at the threat.</summary>
        private static void KeepSensors(ref PeekAndFireState ws, in PeekAndFireParams p, Entity self, EntityRepository world,
                                        in ThreatAim was, in ThreatAim threat)
        {
            // ⭐ CE-3144 (P-8 D14) — the sensors look from and for where the enemy is REMEMBERED (the freshest evidence, D13), never
            //   where he is now: a hidden enemy's true position is seen from nowhere, so both soldiers' searches answered nothing and
            //   the duel stood still (measured live). Re-pointed when that spot moves by RepointMetres or another slot becomes freshest.
            var aim = PostedAim(world, self, in p, SensorAim(world, self, in threat, p.HeardMatchRadius));
            bool retarget = ws.SensorId == 0 || EqsTacticsNodes.Moved(new ThreatAim(Entity.Null, ws.SensorId, ws.SensorAt), in aim);
            var cover = new PostureSensorsParams { SearchRadius = p.SearchRadius, ScoreDeltaThreshold = p.ScoreDeltaThreshold, FactionFilter = p.FactionFilter };
            ws.CoverSensor = PostureNodes.Keep(ws.CoverSensor, world, self, CoverSite, p.HideTemplate, in cover, in aim, retarget);
            if (ws.PeekIsStep == 1)
            {
                var peek = cover;
                peek.SearchRadius = p.PeekSearchRadius;
                ws.PeekSensor = PostureNodes.Keep(ws.PeekSensor, world, self, PeekSite, FindOpenFiringPosition.BlueprintId, in peek, in aim, retarget);
            }
            if (retarget) { ws.SensorId = aim.HeardId; ws.SensorAt = aim.Point; }
            retarget = EqsTacticsNodes.Moved(in was, in threat);
            if (retarget || ws.HeardId != threat.HeardId) ws.HeardPoint = threat.Point;
            ws.Threat = threat.Entity;
            ws.HeardId = threat.HeardId;
        }

        /// <summary>
        /// ⭐ <c>CE-3144</c> (P-8 D14) — the sensors' aim: the freshest evidence (<c>FreshestEvidence</c>) as a POINT, keyed by
        /// the memory slot it came from (the threat's own, or a heard one). Falls back to <paramref name="threat"/> when nothing is
        /// remembered as a spot.
        /// </summary>
        /// <remarks>⭐ <c>CE-2351</c> (Exposure Cycle §4a) — with <paramref name="matchRadius"/>, a heard slot counts only near where THIS
        /// threat is remembered, G4's rule for the burst: 📐 live <c>ua-javelin-lock</c>, sounds of ANOTHER fight 160 m off (a launch, a
        /// missile reaching a friend) pulled a tank's hide and firing queries away from the Javelin team its attack order named.</remarks>
        internal static ThreatAim SensorAim(EntityRepository world, Entity self, in ThreatAim threat, float matchRadius)
            => FreshestEvidence(world, self, in threat, out var at, out long id, matchRadius) && id != 0 ? new ThreatAim(Entity.Null, id, at) : threat;

        /// <summary>⭐ <c>CE-3158</c> G4 — the default <see cref="PeekAndFireParams.HeardMatchRadius"/> (m); the vehicle cycle's sensors use it too
        /// (<c>CE-2351</c>).</summary>
        internal const float DefaultHeardMatchRadius = 15f;

        /// <summary>⭐ <c>CE-2275</c> D-2i — the heard id the sensors carry when they look at the man's SECTOR, not at a threat.</summary>
        internal const long SectorAimId = -2275;

        /// <summary>
        /// ⭐ <c>CE-2275</c> D-2i (PC-14's <c>Allow</c> half, DA-49) — a POSTED man (<c>AnchorRadius</c> &gt; 0) whose threat his sector does not
        /// allow (<see cref="EngagementAreas"/>.<c>Allows</c> — self-defence
        /// included, so a close one still counts) looks for his hide and peek points toward his SECTOR's point instead: a window or a step
        /// that sees the threat outside his arc would take him off his sector (DA-27: the threat he hides from may lie outside it). No
        /// post, no sector, or an allowed threat ⇒ the aim as it was. 📄 DESIGN_Defend_And_Attack_Position.md §8c D-2i.
        /// </summary>
        internal static ThreatAim PostedAim(EntityRepository world, Entity self, in PeekAndFireParams p, in ThreatAim aim)
        {
            if (p.AnchorRadius <= 0f) return aim;
            bool allowed = aim.IsPoint ? EngagementAreas.Allows(world, self, aim.Point)
                         : aim.Entity.IsNull || !world.IsAlive(aim.Entity) || EngagementAreas.Allows(world, self, aim.Entity);
            if (allowed) return aim;
            return EngagementAreas.TryAllowPoint(world, self, out var sector)
                ? new ThreatAim(Entity.Null, SectorAimId, sector) : aim;
        }

        /// <summary>⭐ <c>CE-3158</c> G5 — publishes (and refreshes) the unit's claim on the point it holds or is going to: written when
        /// it COMMITS (the move is issued), so nobody races it there.</summary>
        private static void Claim(ref PeekAndFireState ws, Entity self, EntityRepository world, double now)
        {
            if (ws.Phase == PeekPhase.Choose || !world.IsComponentTypeRegistered<CoverClaim>()) return;
            var claim = new CoverClaim { Hide = ws.HidePoint, Peek = ws.PeekPoint, Until = now + ClaimTtlSeconds };
            if (world.HasComponent<CoverClaim>(self)) world.GetComponentRW<CoverClaim>(self) = claim;
            else world.AddComponent(self, claim);
        }

        /// <summary>⭐ <c>CE-3158</c> G5 — whether <paramref name="at"/> is within <see cref="ClaimRadius"/> (3-D) of another unit's live
        /// claim (hide or peek) of the same force.</summary>
        internal static bool ClaimedByAFriend(EntityRepository world, Entity self, Vector3 at, double now)
        {
            if (self.IsNull || !world.IsComponentTypeRegistered<CoverClaim>()) return false;
            bool forces = world.IsComponentTypeRegistered<EntityInfo>();
            var mine = forces && world.HasComponent<EntityInfo>(self) ? world.GetComponentRO<EntityInfo>(self).ForceId : default;
            foreach (var other in world.Query().With<CoverClaim>().Build())
            {
                if (other == self) continue;
                ref readonly var c = ref world.GetComponentRO<CoverClaim>(other);
                if (c.Until <= now) continue;
                var theirs = forces && world.HasComponent<EntityInfo>(other) ? world.GetComponentRO<EntityInfo>(other).ForceId : default;
                if (!theirs.Equals(mine)) continue;
                if (Vector3.Distance(c.Hide, at) <= ClaimRadius || Vector3.Distance(c.Peek, at) <= ClaimRadius) return true;
            }
            return false;
        }

        /// <summary>B8 — the unit's weapon is reloading, or its magazine is empty (then the reload starts here).</summary>
        private static bool ReloadingOrEmpty(EntityRepository world, Entity self)
        {
            // ⭐ WS-4 (the backend's report) — the unit's PRIMARY weapon: its own, or the station weapon it controls (a bare pickup's M2)
            var gun = Fdp.Toolkit.Combat.WeaponSystems.WeaponOf(world, self);
            if (gun.IsNull) return false;
            ref var weapon = ref world.GetComponentRW<WeaponState>(gun);
            if (Magazine.Reloading(weapon)) return true;
            if (weapon.MagazineSize > 0 && weapon.MagazineRounds == 0 && weapon.Ammo > 0) { Magazine.StartIfEmpty(ref weapon); return true; }
            return false;
        }

        /// <summary>B8/D12 — near-missed or hit within <c>SuppressedSeconds</c>.</summary>
        private static bool Suppressed(EntityRepository world, Entity self, in PeekAndFireParams p)
            => RecentSensesOf.Within(world, self, SensorChange.NearMiss, p.SuppressedSeconds)
            || RecentSensesOf.Within(world, self, SensorChange.Hit, p.SuppressedSeconds);

        /// <summary>B2 — near-missed or hit since the unit came up.</summary>
        /// <summary>⭐ <c>CE-2228</c> — a command-guided round of this unit's aimed fire is in flight: the aimed-fire executor's own answer
        /// (<c>ActionReason.Guiding</c> on its status row, W-12c). The ONE reader — PeekAndFire and <c>VehicleFightFromCover</c> both hold
        /// their gunner on it.</summary>
        /// <summary>⭐ <c>CE-2218</c> — the weapon channel said <c>Masked</c> during this exposure (the row is stamped, so a reason left by the
        /// previous exposure does not count).</summary>
        internal static bool Guiding(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<WeaponChannel>(self)) return false;
            ref readonly var channel = ref world.GetComponentRO<WeaponChannel>(self);
            return channel.ActiveAction == CombatConstants.ActionIdAimAndFire && channel.Status == NodeStatus.Running
                   && world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>()
                   && world.HasComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(self)
                   && world.GetComponentRO<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(self).Weapon.Reason
                      == Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Guiding;
        }

        internal static bool MaskedSince(EntityRepository world, Entity self, double since)
        {
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>()
                || !world.HasComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(self)) return false;
            ref readonly var row = ref world.GetComponentRO<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(self).Weapon;
            return row.Reason == Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Masked && row.At >= since;
        }

        private static bool FiredUponSince(EntityRepository world, Entity self, double since)
        {
            double window = Now(world) - since + 1e-6;
            return RecentSensesOf.WithinSince(world, self, SensorChange.NearMiss, window, since)
                || RecentSensesOf.WithinSince(world, self, SensorChange.Hit, window, since);
        }

        private static void StopBlind(ref PeekAndFireState ws, Entity self, EntityRepository world)
        {
            if (ws.Blind.Started != 0 && world.HasComponent<WeaponChannel>(self))
            {
                ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
                if (channel.ActiveAction == CombatConstants.ActionIdFireAtPoint && channel.ActionInstanceId == ws.Blind.Started)
                {
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
                }
            }
            ws.Blind = default;
        }

        private static void Release(ref PeekAndFireState ws, Entity self, EntityRepository world)
        {
            if (ws.Framed == 1) Leans.Set(world, self, LeanSide.LeanNone);   // ⭐ CE-2275 D-2l — never left leaning
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            StopBlind(ref ws, self, world);
            PostureNodes.Drop(world, ws.CoverSensor);
            PostureNodes.Drop(world, ws.PeekSensor);
            if (ws.Moving == 1) LocomotionMoveTo.Stop(world, self);
            if (world.IsComponentTypeRegistered<CoverClaim>() && world.HasComponent<CoverClaim>(self))
                world.GetComponentRW<CoverClaim>(self).Until = 0d;   // ⭐ CE-3158 G5 — the point is free at once
            ws = default;
        }

        private static double Now(EntityRepository world)
            => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
    }
}
