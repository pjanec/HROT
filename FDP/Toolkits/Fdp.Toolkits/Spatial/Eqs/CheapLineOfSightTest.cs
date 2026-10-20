using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.LineOfSight;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>Which end of the sight line is the eye.</summary>
    public enum EqsLosViewer : byte
    {
        /// <summary>The context entity looks at the candidate ("can the threat see me there?").</summary>
        Slot = 0,
        /// <summary>The self, standing at the candidate, looks at the context entity ("can I shoot from there?").</summary>
        Candidate = 1,
    }

    /// <summary>What the filter keeps.</summary>
    public enum EqsLosRequire : byte
    {
        /// <summary>Keep candidates the line does NOT reach (cover, concealment).</summary>
        Hidden = 0,
        /// <summary>Keep candidates the line reaches (firing positions, threats in view).</summary>
        Visible = 1,
    }

    /// <summary>
    /// ⭐ Line-of-sight FILTER over the resident terrain between each candidate and the entity in
    /// <see cref="ContextSlotIndex"/> (docs/designs/eqs-2/EQS_Design_v1.3_final.md §9.1, §19.5).
    /// <list type="bullet">
    ///   <item>Sight = <see cref="Terrain.TerrainWorld.SegmentBlocked"/> — the occluder perception uses. Heights: an eye at the
    ///     viewer's STANDING height; a context entity is aimed at half its standing height; a positional candidate the threat
    ///     looks at is aimed at the self's CROUCHED eye (the part that must be hidden).</item>
    ///   <item>Flag bit <see cref="ContextSlotIndex"/> = <c>HasLOSToContextN</c> (§4.2), set on every judged candidate's
    ///     <c>FlagsMeaningful</c>.</item>
    ///   <item>⭐ No terrain resident ⇒ sight is unknown ⇒ the test does nothing (no flag judged, nothing rejected).</item>
    ///   <item>The threat-score gate is opt-in: with <see cref="EqsSensor.ThreatThreshold"/> &gt; 0 it reads the self's (else the
    ///     observer's) <see cref="TargetMemory"/> and skips the test while no threat reaches the threshold. Threshold 0 ⇒ always
    ///     filtered (the context slot names the threat explicitly).</item>
    /// </list>
    /// ⛔ SUPERSEDED: the 2-D <c>ILosService</c> whose only implementation (<c>BlockedLosService</c>) answered "blocked"
    /// for everything, a gate on the OBSERVER's <c>TargetMemory</c> (a child sensor's carrier has none ⇒ the test never ran),
    /// and flag bit 0 meaning "covered from slot 1".
    /// </summary>
    public sealed class CheapLineOfSightTest : IEqsTest, IEqsCostWeight
    {
        private readonly ILosService? _los;

        /// <summary>A test over the resident terrain (production: every registry-built template).</summary>
        public CheapLineOfSightTest() { }

        /// <summary>A test over an explicit sight source (a test fake, or a host-specific one such as Stride's raycasts).</summary>
        public CheapLineOfSightTest(ILosService los) => _los = los;

        /// <summary>The context slot (0..2) of the other end of the line. Default 1 = Target.</summary>
        public byte ContextSlotIndex { get; set; } = 1;

        /// <summary>Which end is the eye. Default <see cref="EqsLosViewer.Slot"/>.</summary>
        public EqsLosViewer Viewer { get; set; } = EqsLosViewer.Slot;

        /// <summary>What is kept. Default <see cref="EqsLosRequire.Hidden"/> (cover).</summary>
        public EqsLosRequire Require { get; set; } = EqsLosRequire.Hidden;

        /// <summary>
        /// ⭐ <c>CE-2327</c> (R-2064) — with a candidate eye, also MARK (never reject) the spots a ROUND clears from: <see cref="FireClearBit"/>
        /// when a body point he sees from there is one his weapon's round is not stopped short of (<see cref="Fdp.Toolkit.Combat.LineOfFire.MaskedFrom"/>
        /// — the muscle's own masked rule). 🔒 User: <i>"for peeking the place does not need to allow shooting as that would expose the
        /// soldier too much"</i> — so a spot that only SEES stays an answer (he looks from it); one that also lets him shoot is marked.
        /// </summary>
        public bool MarkFireClear { get; set; }

        /// <summary>⭐ <c>CE-2327</c> — the result flag: a round leaving from this candidate's eye clears what is in front of it.</summary>
        public const short FireClearBit = 1 << 7;

        /// <summary>⭐ <c>CE-2327</c> — an answer's fire mark: 0 = not marked (no terrain, or a template that does not mark) · 1 = a round clears
        /// from it · 2 = it only SEES. The one reader every behaviour uses.</summary>
        public static byte FireMarkOf(in EqsResult r)
            => (r.FlagsMeaningful & FireClearBit) == 0 ? (byte)0 : (r.Flags & FireClearBit) != 0 ? (byte)1 : (byte)2;

        /// <inheritdoc/>
        /// <inheritdoc/>
        public int CostPerCandidate => EqsCost.Sight;   // CE-3037 — sight weight (DESIGN_Sensors_And_Doctrine §5.3)

        public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

        /// <inheritdoc/>
        public unsafe void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var los = EqsTerrainSight.Sight(view, _los);
            if (los == null) return;

            // ⭐ CE-3063 ③ — the other side is an entity, or a heard contact's POINT (then it has the default mount).
            if (!EqsContext.AnchorPosition(view, observer, sensor, ContextSlotIndex, out var otherPos)) return;
            var other = EqsContext.Anchor(view, observer, sensor, ContextSlotIndex);
            var self = EqsContext.Self(view, observer, sensor);

            // ⭐ The gate is OPT-IN (ThreatThreshold > 0): a sensor that names its threat in the slot and sets no threshold is
            //   always filtered — an empty memory on the Muscle used to switch the filter off (found by the cross-node rail).
            var memOwner = sensor.ThreatThreshold > 0f ? EqsContext.ThreatMemoryOwner(view, observer, self) : Entity.Null;
            if (!memOwner.IsNull)
            {
                ref readonly var mem = ref view.GetComponentRO<TargetMemory>(memOwner);
                if (mem.Count == 0 || mem.Freshness[0] < sensor.ThreatThreshold) return;
            }

            // ⭐ CE-2327 — the fire mark needs the round's world (the muscle's terrain) and the unit's weapon; none ⇒ unmarked
            var fire = MarkFireClear && Viewer == EqsLosViewer.Candidate && view is EntityRepository repo && !self.IsNull ? repo : null;
            var fireTerrain = fire != null ? Fdp.Toolkit.World.WorldQuery.Of(fire) : null;
            if (fireTerrain == null) fire = null;

            var otherMount = EqsTerrainSight.Mount(view, other);
            var selfMount  = EqsTerrainSight.Mount(view, self);
            short bit      = (short)(1 << ContextSlotIndex);

            // ⭐ CE-3144 (P-8) — "can I shoot him from there" looks at the other's BODY POINTS for its logical stance and is clear when
            //   ANY is: perception's rule (TerrainWorldLosStrategy.IsVisible, Buildings §3f/§3i). A point (a remembered or heard spot)
            //   has no stance: a standing man's points. ⛔ SUPERSEDED: one line to half the standing eye (0.85 m) — below a 0.9 m sill,
            //   so no window ever saw a man at another window.
            Span<float> aims = stackalloc float[4];
            int aimCount = 0;
            if (Viewer == EqsLosViewer.Candidate)
            {
                var stance = other.IsNull ? Fdp.Toolkit.Tkb.Domain.StanceId.Standing : Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(view, other);
                float hull = other.IsNull ? 0f : Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.HullHeight(view, other);
                float scale = hull > 0f ? hull : otherMount.For(stance);
                foreach (float f in BodyProfile.Fractions(stance, hull > 0f))
                    if (aimCount < aims.Length) aims[aimCount++] = f * scale;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId == -1L) continue;

                var cPos = new Vector3(c.PositionX, c.PositionY, c.PositionZ);
                bool visible;
                if (Viewer == EqsLosViewer.Candidate)
                {
                    // ⭐ CE-3135 (peek-and-fire D5) — the eye at the candidate is the POINT's stance when it has one (a window
                    //   point is crouched: standing there would see over a sill nobody fires over); none ⇒ standing, as before.
                    float eye = c.TryGetStance(out var stance) ? selfMount.For(stance) : selfMount.Standing;
                    var from = cPos + new Vector3(0, 0, eye);
                    visible = false;
                    bool fireClear = false;
                    for (int k = 0; k < aimCount && !(visible && (fireClear || fire == null)); k++)
                    {
                        var at = otherPos + new Vector3(0, 0, aims[k]);
                        if (!los.HasLineOfSight(from, at)) continue;
                        visible = true;
                        // ⭐ CE-2327 — a point he sees is one he can SHOOT when the round from his eye is not stopped short
                        if (fire != null && !Fdp.Toolkit.Combat.LineOfFire.MaskedFrom(fire, fireTerrain!, self, 0, from, at)) fireClear = true;
                    }
                    if (fire != null)
                    {
                        c.FlagsMeaningful |= FireClearBit;
                        if (fireClear) c.Flags |= FireClearBit;
                    }
                }
                else
                {
                    float aim = c.EntityId > 0
                        ? EqsTerrainSight.Mount(view, new Entity((ulong)c.EntityId)).Standing * 0.5f
                        : selfMount.Crouched;
                    visible = los.HasLineOfSight(
                        otherPos + new Vector3(0, 0, otherMount.Standing),
                        cPos + new Vector3(0, 0, aim));
                }

                c.FlagsMeaningful |= bit;
                if (visible) c.Flags |= bit;
                if (visible != (Require == EqsLosRequire.Visible)) c.EntityId = -1L;
            }
        }
    }
}
