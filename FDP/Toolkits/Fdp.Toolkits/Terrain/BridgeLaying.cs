using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>A laid bridge as planned between two banks: where its deck goes and where the engineer stands to lay it — or why it cannot be laid.</summary>
    public readonly struct LaidDeckPlan
    {
        /// <summary><see cref="BridgeRefusal.NotRefused"/> when the bridge can be laid; else why it is refused (<see cref="BridgeLaying.Say"/> words it).</summary>
        public BridgeRefusal Refusal { get; init; }
        /// <summary>The deck's centre (the obstacle's position) and heading (rad, from +X).</summary>
        public Vector2 Centre { get; init; }
        public float Yaw { get; init; }
        /// <summary>The deck between its two ends (the obstacle's <see cref="ObstacleShape.Length"/>), m.</summary>
        public float Length { get; init; }
        /// <summary>The water gap measured along the line (bank to bank), m.</summary>
        public float Gap { get; init; }
        /// <summary>Where the engineer's hull stands to lay it (short of the near ramp's foot), and a point behind it on the same line (the approach that aligns the hull).</summary>
        public Vector2 Stand { get; init; }
        public Vector2 Approach { get; init; }
        public bool Ok => Refusal == BridgeRefusal.NotRefused;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4693</c> (bridge laying BL-S3, BL-11, BL-20; 📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §12b) — THE ONE place a laid bridge's deck pose and its
    /// refusal are computed: the order's refusal (the engineer's behaviour) and the mover's spawn (<see cref="BridgeWorkSystem"/>) both ask <see cref="Plan"/>, so they
    /// cannot disagree. A deck runs along the line NearBank → FarBank, centred on the water gap it crosses, as long as the gap plus a bearing on each bank (capped
    /// at the bridge type's own length — BL-20). ⛔ Refused (with its reason) when the gap is wider than the engineer's <see cref="BridgeLayerDto.SpanMaxM"/>, when a bank
    /// or a deck end is in the water, or when there is no water between the banks.
    /// </summary>
    public static class BridgeLaying
    {
        /// <summary>The water is sampled along the line at this step (m).</summary>
        public const float SampleMetres = 0.25f;
        /// <summary>The least a deck end rests on the bank beyond the water (m).</summary>
        public const float MinBearingMetres = 1f;
        /// <summary>The hull stands this far short of the near ramp's foot (m), and approaches from this far behind its stand.</summary>
        public const float StandClearMetres = 2f, ApproachMetres = 10f;
        /// <summary>How near its stand point the hull must be for the work to go on (m).</summary>
        public const float StandReachMetres = 6f;
        /// <summary>A laid deck within this of a plan's centre (m) is that plan's bridge.</summary>
        public const float SamePlaceMetres = 3f;

        /// <summary>The refusal in words, for whoever gave the order (the log, the order's reply). ⭐ Invariant culture (R-4221).</summary>
        public static string Say(BridgeRefusal why, float gap, float spanMax) => why switch
        {
            BridgeRefusal.NotRefused => "",
            BridgeRefusal.NoTerrain => "no terrain is loaded",
            BridgeRefusal.SamePlace => "the two banks are the same place",
            BridgeRefusal.NearBankInWater => "the near bank is in the water",
            BridgeRefusal.FarBankInWater => "the far bank is in the water",
            BridgeRefusal.NoWater => "there is no water between the two banks",
            BridgeRefusal.GapTooWide => string.Format(CultureInfo.InvariantCulture, "the gap is {0:0.0} m, wider than the {1:0.0} m this bridge spans", gap, spanMax),
            BridgeRefusal.DeckEndInWater => "a deck end would stand in the water",
            BridgeRefusal.NoSpanCarried => "this engineer has no span on board",
            BridgeRefusal.NotItsBridge => "that is not a laid bridge this engineer can carry",
            _ => why.ToString(),
        };

        /// <summary>The engineer's figures and its bridge type's: false when the engineer type carries no <see cref="BridgeLayerDto"/> (or its bridge type is unknown).</summary>
        public static bool Figures(ITkbDatabase? tkb, long engineerType, out BridgeLayerDto layer, out StaticObstacleDto span)
        {
            layer = null!; span = null!;
            if (tkb == null || !tkb.TryGetByType(engineerType, out var t) || t.GetDescriptor<BridgeLayerDto>() is not { } l) return false;
            if (!tkb.TryGetByType(l.BridgeType, out var b) || b.GetDescriptor<StaticObstacleDto>() is not { } s) return false;
            layer = l; span = s;
            return true;
        }

        /// <summary>The engineer's figures by its <c>TkbIdentity</c>.</summary>
        public static bool Figures(EntityRepository world, Entity engineer, out BridgeLayerDto layer, out StaticObstacleDto span)
        {
            layer = null!; span = null!;
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>() || !world.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(engineer)) return false;
            var tkb = world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
            return Figures(tkb, (long)world.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(engineer).TkbType, out layer, out span);
        }

        /// <summary>The ramp beyond each deck end for a deck <paramref name="deckHeight"/> above the bank (terrain's rule, <see cref="DeckGeometry.RampLengthFor"/>).</summary>
        public static float RampOf(float deckHeight) => DeckGeometry.RampLengthFor(deckHeight);

        /// <summary>
        /// Plans a bridge from <paramref name="near"/> to <paramref name="far"/> on <paramref name="terrain"/>: the engineer can bridge <paramref name="spanMax"/> of water with a
        /// deck at most <paramref name="typeLength"/> long, <paramref name="deckHeight"/> above the banks; its hull is <paramref name="hullHalf"/> from centre to front.
        /// </summary>
        public static LaidDeckPlan Plan(TerrainWorld? terrain, Vector2 near, Vector2 far, float spanMax, float typeLength, float deckHeight, float hullHalf)
        {
            if (terrain == null) return new LaidDeckPlan { Refusal = BridgeRefusal.NoTerrain };
            var d = far - near;
            float len = d.Length();
            if (len < 1f) return new LaidDeckPlan { Refusal = BridgeRefusal.SamePlace };
            var u = d / len;
            if (InWater(terrain, near)) return new LaidDeckPlan { Refusal = BridgeRefusal.NearBankInWater };
            if (InWater(terrain, far)) return new LaidDeckPlan { Refusal = BridgeRefusal.FarBankInWater };

            float first = -1f, last = -1f;
            for (float s = 0f; s <= len; s += SampleMetres)
                if (InWater(terrain, near + u * s)) { if (first < 0f) first = s; last = s; }
            if (first < 0f) return new LaidDeckPlan { Refusal = BridgeRefusal.NoWater };
            float gap = last - first + SampleMetres;
            if (gap > spanMax)
                return new LaidDeckPlan { Gap = gap, Refusal = BridgeRefusal.GapTooWide };

            float bearing = MathF.Max(MinBearingMetres, (typeLength - spanMax) * 0.5f);
            float length = MathF.Min(typeLength, gap + 2f * bearing);
            var centre = near + u * ((first + last) * 0.5f);
            var nearEnd = centre - u * (length * 0.5f);
            var farEnd = centre + u * (length * 0.5f);
            if (InWater(terrain, nearEnd) || InWater(terrain, farEnd))
                return new LaidDeckPlan { Gap = gap, Refusal = BridgeRefusal.DeckEndInWater };

            var stand = nearEnd - u * (RampOf(deckHeight) + hullHalf + StandClearMetres);
            return new LaidDeckPlan
            {
                Centre = centre, Yaw = MathF.Atan2(u.Y, u.X), Length = length, Gap = gap,
                Stand = stand, Approach = stand - u * ApproachMetres,
            };
        }

        /// <summary>
        /// Where the engineer stands to RECOVER a laid deck (centre <paramref name="centre"/>, heading <paramref name="yaw"/>, <paramref name="length"/> between its ends): short of the
        /// ramp foot at the deck end nearer <paramref name="from"/>, and the point behind it (its approach).
        /// </summary>
        public static (Vector2 Stand, Vector2 Approach) RecoverStand(Vector2 centre, float yaw, float length, float deckHeight, float hullHalf, Vector2 from)
        {
            var (a, b) = DeckGeometry.Ends(centre, yaw, length);
            var end = Vector2.DistanceSquared(a, from) <= Vector2.DistanceSquared(b, from) ? a : b;
            var outward = end - centre;
            outward = outward.LengthSquared() > 1e-6f ? Vector2.Normalize(outward) : Vector2.UnitX;
            var stand = end + outward * (RampOf(deckHeight) + hullHalf + StandClearMetres);
            return (stand, stand + outward * ApproachMetres);
        }

        /// <summary>An ACTIVE (baked, open) walkable-deck obstacle of TKB type <paramref name="bridgeType"/> within <see cref="SamePlaceMetres"/> of <paramref name="centre"/>, else null.
        /// <paramref name="query"/> is the caller's cached <c>StaticObstacle + SimTransform</c> query (built once per world).</summary>
        public static Entity OpenBridgeAt(EntityRepository world, EntityQuery query, long bridgeType, Vector2 centre)
        {
            bool typed = world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            foreach (var e in query)
            {
                if (world.GetComponentRO<StaticObstacle>(e).Kind != ObstacleKind.WalkableDeck) continue;
                if (typed && world.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(e)
                    && (long)world.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType != bridgeType) continue;
                var p = world.GetComponentRO<SimTransform>(e).Position;
                if (Vector2.Distance(new Vector2(p.X, p.Y), centre) <= SamePlaceMetres) return e;
            }
            return Entity.Null;
        }

        /// <summary>The query <see cref="OpenBridgeAt"/> walks (Active obstacles only — a Constructing deck is not open yet, R-243).</summary>
        public static EntityQuery OpenObstacles(EntityRepository world) => world.Query().With<StaticObstacle>().With<SimTransform>().Build();

        /// <summary>Whether <paramref name="p"/> is under authored water (a Water surface of the terrain).</summary>
        public static bool InWater(TerrainWorld terrain, Vector2 p)
        {
            var surfaces = terrain.Surfaces;
            for (int i = 0; i < surfaces.Count; i++)
            {
                var s = surfaces[i];
                if (s.Type != TerrainSurfaceType.Water) continue;
                if (p.X < s.Min.X || p.Y < s.Min.Y || p.X > s.Max.X || p.Y > s.Max.Y) continue;
                if (PolygonMath.Contains(s.Polygon, p)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4693</c> (BL-S3) — projects <see cref="BridgeLayerDto"/>: a bridge engineer starts with its spans on board — <see cref="BridgeCarried"/> with
    /// <see cref="BridgeLayerDto.Carried"/> spans of <see cref="BridgeLayerDto.BridgeType"/>, no order. ⭐ Added WITH its final value at creation, so the brain only
    /// ever SETS it (never a structural change in the parallel phase — D-31). An instance's own value (a recording's) wins.
    /// </summary>
    public sealed class BridgeLayerTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors() { yield return typeof(BridgeLayerDto); }

        public IEnumerable<Type> GetProducedComponents() { yield return typeof(BridgeCarried); }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            if (template.GetDescriptor<BridgeLayerDto>() is not { } dto || !repo.IsComponentTypeRegistered<BridgeCarried>() || repo.HasComponent<BridgeCarried>(entity)) return;
            repo.AddComponent(entity, new BridgeCarried { Count = (byte)Math.Clamp(dto.Carried, 0, 255), BridgeType = dto.BridgeType });
        }
    }
}
