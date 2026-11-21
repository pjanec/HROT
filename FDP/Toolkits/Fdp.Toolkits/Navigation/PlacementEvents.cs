using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐ MACROS S3a ⑤ — PLACEMENT as a navigation-solver query (docs/DESIGN_Scenario_Macros.md §14.2 SR-180…SR-183, §14.3 SR-184/185, §14.4 SR-190).
    // The expander (Hrot.Common) asks "where may these N things stand, near these points?" and the solver — the one owner of the navmesh and
    // the terrain a node holds — answers per slot: Exact · Shifted (within MaxShift) · Failed. A LIST per request ⇒ MANAGED events (classes,
    // PublishManagedEvent / RegisterManagedEvent), the shape of PathfindingEvents.cs but batched. Wire twins: PlacementRequestBatchWire /
    // PlacementResponseBatchWire (dtPlacementRequestBatch 180 · dtPlacementResponseBatch 181, Hrot.Network.NED) — R-134, no DDS type here.
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>⭐ SR-181 — which ground a placed thing stands on: picks the navmesh layer (and whether water is acceptable).</summary>
    public enum PlacementLayer : byte
    {
        /// <summary>A man on foot — the <see cref="NavLayerMask.Infantry"/> layer; not on water.</summary>
        Infantry = 0,
        /// <summary>A ground vehicle — the <see cref="NavLayerMask.Vehicle"/> layer; not on water.</summary>
        Vehicle = 1,
        /// <summary>Land or water — the vehicle and naval layers; water is acceptable.</summary>
        Amphibious = 2,
        /// <summary>Airborne — the point is taken as is.</summary>
        Air = 3,
    }

    /// <summary>⭐ SR-181 — how a slot was answered.</summary>
    public enum PlacementOutcome : byte
    {
        /// <summary>The requested point (within 0.25 m).</summary>
        Exact = 0,
        /// <summary>A different point within <see cref="PlacementRequestBatch.MaxShift"/>.</summary>
        Shifted = 1,
        /// <summary>No acceptable ground within <see cref="PlacementRequestBatch.MaxShift"/>; <see cref="PlacementAnswer.Reason"/> says why.</summary>
        Failed = 2,
    }

    /// <summary>One thing to place: the macro-local <paramref name="Key"/> echoed in its answer, the wanted <see cref="Point"/> (FDP metres), its
    /// layer, and <see cref="Radius"/> — half its footprint, the spacing it keeps from every other thing.</summary>
    public sealed class PlacementSlot
    {
        public int Key;
        public Vector3 Point;
        public PlacementLayer Layer;
        public float Radius;
        /// <summary>⭐ Macros debt A3 (SR-157 / SR-180) — the network id of the entity this slot MOVES (a reconcile re-placing a kept child of a
        /// moved instance); 0 = a new entity. Every slot's <see cref="Self"/> is ignored as an obstacle for the whole batch, so a moved team is
        /// not pushed away by where it stood before.</summary>
        public int Self;
    }

    /// <summary>⭐ SR-180 — a request: place every <see cref="Slots"/> entry, none farther than <see cref="MaxShift"/> metres (XY) from its point.
    /// Managed (a list) — published with <c>PublishManagedEvent</c>, registered with <c>RegisterManagedEvent</c>.</summary>
    public sealed class PlacementRequestBatch
    {
        /// <summary>Echoed in the response.</summary>
        public long RequestId;
        /// <summary>The asking node — the response is routed back to it.</summary>
        public NodeId SourceNodeId;
        /// <summary>The farthest (XY metres) a slot may move.</summary>
        public float MaxShift;
        public List<PlacementSlot> Slots = new();
    }

    /// <summary>The answer for one slot: its <paramref name="Key"/>, the <paramref name="Outcome"/>, the point to use (the requested one when Failed)
    /// and a <paramref name="Reason"/> (empty when Exact on ground; says why on Failed; "no terrain" when nothing could be checked).</summary>
    public sealed class PlacementAnswer
    {
        public int Key;
        public PlacementOutcome Outcome;
        public Vector3 Point;
        public string Reason = string.Empty;
    }

    /// <summary>⭐ SR-180 — ONE response per request: same <see cref="RequestId"/> and <see cref="SourceNodeId"/>, an answer per slot in the request's order.</summary>
    public sealed class PlacementResponseBatch
    {
        public long RequestId;
        public NodeId SourceNodeId;
        public List<PlacementAnswer> Answers = new();
    }
}
