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
                    for (int k = 0; k < aimCount && !visible; k++)
                        visible = los.HasLineOfSight(from, otherPos + new Vector3(0, 0, aims[k]));
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
