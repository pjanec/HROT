namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-2165</c> V3 (docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §3) — where a VEHICLE hides: cover points sized
    /// by its hull (<see cref="CoverPointsGenerator.VehicleFootprint"/>) that the target in slot 1 cannot see, hidden from every known threat
    /// above all, near and reachable.
    /// </summary>
    [EqsTemplate(AssetId)]
    public static class FindVehicleHide
    {
        public const string AssetId = "6b3eadf0-2420-4015-8842-56cb513196ae";
        public const uint BlueprintId = 0x10B0D9B3u;

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new CoverPointsGenerator { VehicleFootprint = true },
            FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest { ContextSlotIndex = 1, Viewer = EqsLosViewer.Slot, Require = EqsLosRequire.Hidden } },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive = new IEqsTest[] { new ThreatExposureTest { Weight = ThreatExposureTest.HideFromAllWeight }, new PathCostScoreTest() },
            MaxCandidates  = 32,
        };
    }

    /// <summary>
    /// ⭐ <c>CE-2165</c> V3 (§3) — where a vehicle shows itself to fire from: a donut round its hide (slot 0) that SEES the target (slot 1),
    /// straight out from the hide toward him (a straight drive out, a straight reverse back), outside the reach of the threats that can
    /// hurt it (<see cref="BeyondThreatRangeTest"/> — reads the TKB the host hands it), seen by as few other threats as possible, reachable.
    /// </summary>
    [EqsTemplate(AssetId)]
    public static class FindVehicleFiringPoint
    {
        public const string AssetId = "5f1fe7c5-d2f7-4db9-a62d-aec15875986b";
        public const uint BlueprintId = 0xF15555E6u;

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new DonutGenerator { AnchorSlot = 0, Rings = 2, PointsPerRing = 16, InnerFraction = 0.3f },
            FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest { ContextSlotIndex = 1, Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible } },
            ScoreCheap     = new IEqsTest[] { new DotProductTest { PivotSlot = 0, ReferenceSlot = 1, PreferredDot = 1f } },
            ScoreExpensive = new IEqsTest[] { new BeyondThreatRangeTest(), new ThreatExposureTest(), new PathCostScoreTest() },
            MaxCandidates  = 32,
        };
    }

    /// <summary>
    /// ⭐ <c>CE-2165</c> V3 / V8 (§3, §8) — where a vehicle shelters from a DRONE: the nearest reachable grid point with something solid
    /// above its hull (<see cref="OverheadCoverTest"/> — a bridge deck, a roof slab). Open ground answers nothing; the caller decides.
    /// </summary>
    [EqsTemplate(AssetId)]
    public static class FindShelter
    {
        public const string AssetId = "d62f86da-e449-4986-b48b-e80af97e36f8";
        public const uint BlueprintId = 0x072E6926u;

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId     = BlueprintId,
            Generator       = new GridGenerator { AnchorSlot = 0, Spacing = 5f },
            FilterExpensive = new IEqsTest[] { new OverheadCoverTest() },
            ScoreCheap      = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive  = new IEqsTest[] { new PathCostScoreTest() },
            MaxCandidates   = 128,
        };
    }
}
