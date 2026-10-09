namespace Fdp.Toolkit.Spatial.Eqs
{
    // ⭐ §6.6 starter pack, positional half (docs/designs/eqs-2/EQS_Design_v1.3_final.md §19.6). Sensor inputs, all templates:
    //   ContextSlot0 = self (optional — a child sensor resolves its parent), ContextSlot1 = the target,
    //   SearchRadius = how far to look, FactionFilter = which forces count as threats (0 = every acquired contact).
    //   Each BlueprintId = FNV-1a over its AssetId (a rail pins it to EqsTemplateRegistry.BlueprintIdOf).

    /// <summary>⭐ Where to shoot the target from: ring points around the self that SEE the target (standing eye), nearer and
    /// higher score more, the less exposed to other known threats the better, unreachable dropped.</summary>
    [EqsTemplate(AssetId)]
    public static class FindOpenFiringPosition
    {
        public const string AssetId = "aaf2fa6d-2ecc-4e6c-b7e9-5d14b1d7be2e";
        public const uint BlueprintId = 0xE045506Bu;

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new DonutGenerator { AnchorSlot = 0, Rings = 3, PointsPerRing = 16, InnerFraction = 0.2f },
            FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest { Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible } },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest(), new HeightScoreTest { Weight = 0.5f } },
            ScoreExpensive = new IEqsTest[] { new ThreatExposureTest(), new PathCostScoreTest() },
            MaxCandidates  = 48,
        };
    }

    /// <summary>⭐ Stage 7a — where to shoot the target from INSIDE a building: the window firing positions near the self (any
    /// storey — <see cref="TerrainCoverProvider"/> puts one on the inside face of each window, at the lowest stance that clears
    /// the sill) whose standing eye SEES the target, nearer scoring more, then exposure and path cost.
    /// 📄 docs/DESIGN_Building_Interiors.md §3l C5.</summary>
    [EqsTemplate(AssetId)]
    public static class FindWindowFiringPosition
    {
        public const string AssetId = "c4978acd-452f-4b9d-b7e9-47c95484702b";
        public const uint BlueprintId = 0xE29A2384u;

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new CoverPointsGenerator { Kind = CoverKind.WindowFiring },
            FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest { Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible } },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive = new IEqsTest[] { new ThreatExposureTest(), new PathCostScoreTest() },
            MaxCandidates  = 32,
        };
    }

    /// <summary>⭐ A flank on the target: ring points around the TARGET that see it, scored by bearing — side-on to the
    /// target→self line scores most — then exposure and path cost.</summary>
    [EqsTemplate(AssetId)]
    public static class FindFlankingPosition
    {
        public const string AssetId = "c59328e6-984b-4e01-9a47-924d46cbdfd7";
        public const uint BlueprintId = 0xC33075C4u;

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new DonutGenerator { AnchorSlot = 1, Rings = 2, PointsPerRing = 24, InnerFraction = 0.5f },
            FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest { Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible } },
            ScoreCheap     = new IEqsTest[] { new DotProductTest { PivotSlot = 1, ReferenceSlot = 0, PreferredDot = 0f, Weight = 2f } },
            ScoreExpensive = new IEqsTest[] { new ThreatExposureTest(), new PathCostScoreTest() },
            MaxCandidates  = 48,
        };
    }

    /// <summary>⭐ Somewhere safe to fall back to: grid points around the self that the target CANNOT see, farther from the
    /// target scores more, hidden from every known threat scores more, unreachable dropped.</summary>
    [EqsTemplate(AssetId)]
    public static class FindSafeRetreatPoint
    {
        public const string AssetId = "c83b25e7-2af8-47d8-bffe-30f01169e7a0";
        public const uint BlueprintId = 0xB54DED5Du;

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new GridGenerator { AnchorSlot = 0, Spacing = 5f },
            FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest { Viewer = EqsLosViewer.Slot, Require = EqsLosRequire.Hidden } },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest { FromSlot = 1, PreferFar = true } },
            ScoreExpensive = new IEqsTest[] { new ThreatExposureTest(), new PathCostScoreTest() },
            MaxCandidates  = 128,
        };
    }

    /// <summary>⭐ The entities of the filtered forces the self can SEE from where it stands, nearest first.</summary>
    [EqsTemplate(AssetId)]
    public static class FindThreatsInView
    {
        public const string AssetId = "f344aa2b-77b6-4200-844d-b2797e6df7d9";
        public const uint BlueprintId = 0xF9E1988Fu;

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId   = BlueprintId,
            Generator     = new EntitiesInRadiusGenerator(),
            FilterCheap   = new IEqsTest[]
            {
                new FactionFilterTest(), new AliveFilterTest(),
                new CheapLineOfSightTest { ContextSlotIndex = 0, Viewer = EqsLosViewer.Slot, Require = EqsLosRequire.Visible },
            },
            ScoreCheap    = new IEqsTest[] { new DistanceScoreTest() },
            MaxCandidates = 64,
        };
    }
}
