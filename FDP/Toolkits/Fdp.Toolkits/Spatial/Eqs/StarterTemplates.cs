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
            FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest { Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible, MarkFireClear = true } },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest(), new HeightScoreTest { Weight = 0.5f } },
            ScoreExpensive = new IEqsTest[] { new ThreatExposureTest(), new PathCostScoreTest() },
            MaxCandidates  = 48,
        };
    }

    /// <summary>⭐ Stage 7a — where to shoot the target from INSIDE a building: the window firing positions near the self (any
    /// storey — <see cref="TerrainCoverProvider"/> puts one on the inside face of each window, at the lowest stance that clears
    /// the sill) whose eye AT THAT STANCE sees the target (CE-3135, the answer carries the stance), nearer scoring more, then exposure and path cost.
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

    /// <summary>⭐ <c>CE-2275</c> D-1 (DA-34) — a squad's defensive window POSTS: the window firing positions of every storey near the self
    /// (the man standing at the defended position) whose eye at the point's stance sees the engagement area's point (slot 1, a point),
    /// nearer scoring more. The squad play picks one per man from the answer (3-D spacing) and keeps the rest as alternates.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8c.</summary>
    [EqsTemplate(AssetId)]
    public static class FindDefensiveWindowPosts
    {
        public const string AssetId = "3b7e2d61-9c4a-4f0e-b8d2-5a1c6e9f7d03";
        public const uint BlueprintId = 0xB0DC4C8Au;   // EqsTemplateRegistry.BlueprintIdOf(AssetId)

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new CoverPointsGenerator { Kind = CoverKind.WindowFiring },
            FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest { Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible } },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive = new IEqsTest[] { new PathCostScoreTest() },
            MaxCandidates  = 48,
        };
    }

    /// <summary>⭐ <c>CE-2275</c> D-1 (DA-36) — the all-round defence (no engagement area): every window firing position of every storey near
    /// the self, nearer scoring more — the squad play spreads its men over them.</summary>
    [EqsTemplate(AssetId)]
    public static class FindWindowPostsAllRound
    {
        public const string AssetId = "7d4c1a92-2e6b-4b3f-9a85-c0f1d2e3b4a6";
        public const uint BlueprintId = 0xF87B5982u;   // EqsTemplateRegistry.BlueprintIdOf(AssetId)

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new CoverPointsGenerator { Kind = CoverKind.WindowFiring },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive = new IEqsTest[] { new PathCostScoreTest() },
            MaxCandidates  = 48,
        };
    }

    /// <summary>⭐ <c>CE-2275</c> D-2b (DA-6, DA-41) — the SET-BACK twin of <see cref="FindDefensiveWindowPosts"/>: each window point set back
    /// 2 m (else 1.5 m, else at the sill) along the line through the window, kept when its eye still sees the engagement area's point. A
    /// Regular-or-better squad's window posts. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8c D-2b.</summary>
    [EqsTemplate(AssetId)]
    public static class FindDefensiveWindowSetBackPosts
    {
        public const string AssetId = "3669bcc9-aad4-455f-bb9b-ad90354f7e12";
        public const uint BlueprintId = 0xE4B1BBB2u;   // EqsTemplateRegistry.BlueprintIdOf(AssetId)
        public const float SetBackMetres = 2f;

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new CoverPointsGenerator { Kind = CoverKind.WindowFiring, SetBackMetres = SetBackMetres },
            FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest { Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible } },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive = new IEqsTest[] { new PathCostScoreTest() },
            MaxCandidates  = 48,
        };
    }

    /// <summary>⭐ <c>CE-2275</c> D-2b — the set-back twin of <see cref="FindWindowPostsAllRound"/> (no engagement area).</summary>
    [EqsTemplate(AssetId)]
    public static class FindWindowSetBackPostsAllRound
    {
        public const string AssetId = "8d5cdcea-345e-4266-92d1-b7424f818c6a";
        public const uint BlueprintId = 0x108713F8u;   // EqsTemplateRegistry.BlueprintIdOf(AssetId)

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new CoverPointsGenerator { Kind = CoverKind.WindowFiring, SetBackMetres = FindDefensiveWindowSetBackPosts.SetBackMetres },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive = new IEqsTest[] { new PathCostScoreTest() },
            MaxCandidates  = 48,
        };
    }

    /// <summary>⭐ <c>CE-2275</c> D-2b — the set-back twin of <see cref="FindWindowFiringPosition"/>: a set-back window man's own hide ask (his
    /// <c>PeekAndFire</c>), so he rotates between set-back posts, never to the sill.</summary>
    [EqsTemplate(AssetId)]
    public static class FindWindowSetBackPosition
    {
        public const string AssetId = "c1c6d31a-cd0b-4a4b-8450-881418add1eb";
        public const uint BlueprintId = 0xD3FE6EADu;   // EqsTemplateRegistry.BlueprintIdOf(AssetId)

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new CoverPointsGenerator { Kind = CoverKind.WindowFiring, SetBackMetres = FindDefensiveWindowSetBackPosts.SetBackMetres },
            FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest { Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible } },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive = new IEqsTest[] { new ThreatExposureTest(), new PathCostScoreTest() },
            MaxCandidates  = 32,
        };
    }

    /// <summary>⭐ <c>CE-2275</c> D-2g (DA-5, DA-22, DA-47) — ROOF posts: cover points on a roof (behind its parapet, beside its stair house,
    /// <see cref="CoverPointsGenerator.RoofOnly"/>) whose eye sees the engagement area's point (context slot 1); nearer scores more, an
    /// unreachable roof drops out on its path cost (DA-5: "only if the roof is reachable"). The squad's second post ask, after the windows.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8c D-2g.</summary>
    [EqsTemplate(AssetId)]
    public static class FindRoofPosts
    {
        public const string AssetId = "2b048d56-9b77-4b09-9fc3-18efebd5e034";
        public const uint BlueprintId = 0xF7D70244u;   // EqsTemplateRegistry.BlueprintIdOf(AssetId)

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new CoverPointsGenerator { RoofOnly = true },
            FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest { Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible } },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive = new IEqsTest[] { new PathCostScoreTest() },
            MaxCandidates  = 48,
        };
    }

    /// <summary>⭐ <c>CE-2342</c> (R-2066 A) — a squad's COVER POSTS facing the engagement area's point (slot 1): the cover points near the self
    /// with an obstacle between them and that point (<see cref="CoverTowardTest"/> — low cover counts, the man fires over it crouched), nearer
    /// scoring more, then hidden from every known threat, then path cost. ⛔ Not <see cref="FindCoverFromTarget"/>: its crouched-eye check
    /// keeps neither face of a 1 m berm from a point in the open. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8f.2.</summary>
    [EqsTemplate(AssetId)]
    public static class FindCoverPosts
    {
        public const string AssetId = "3907f5ee-9ba0-401e-b238-48fe8312c318";
        public const uint BlueprintId = 0x61F46F45u;   // EqsTemplateRegistry.BlueprintIdOf(AssetId)

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new CoverPointsGenerator(),
            FilterCheap    = new IEqsTest[] { new CoverTowardTest() },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive = new IEqsTest[] { new ThreatExposureTest { Weight = ThreatExposureTest.HideFromAllWeight }, new PathCostScoreTest() },
            MaxCandidates  = 48,
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

    /// <summary>
    /// ⭐ <c>WL-4</c> W-12f (📄 docs/DESIGN_Weapon_Layers.md §5f L-5/L-6) — where to THROW from: ring points around slot 1 — the
    /// target's LAST-KNOWN point (the node points the sensor there, never at the live entity: the thrower cannot know where a
    /// hidden man is now) — between 35 % and 100 % of the search radius (the node sets ~18 m, inside an M67's reach and outside
    /// its burst), nearer the unit scoring more (a short dash), then exposure and path cost. ⚠ No "hidden" filter: the sight test
    /// judges a standing man, who sees over the 1.2 m wall a crouched thrower hides behind (§5f).
    /// </summary>
    [EqsTemplate(AssetId)]
    public static class FindThrowingPosition
    {
        public const string AssetId = "29715528-c08c-4277-b723-bc40624bd70c";
        public const uint BlueprintId = 0xA893E4F3u;

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new DonutGenerator { AnchorSlot = 1, Rings = 2, PointsPerRing = 24, InnerFraction = 0.35f },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest { FromSlot = 0 } },
            ScoreExpensive = new IEqsTest[] { new ThreatExposureTest(), new PathCostScoreTest() },
            MaxCandidates  = 48,
        };
    }

    /// <summary>⭐ Somewhere safe to fall back to: grid points around the self that the target CANNOT see, farther from the
    /// target scores more, hidden from every known threat scores more, unreachable dropped. ⭐ <c>CE-2164</c> (R-3004) — on open
    /// ground, where the target sees every point, it still answers: the least exposed, farthest, reachable point
    /// (<see cref="EqsQueryTemplate.KeepAllWhenFilteredOut"/>) — a squad breaking contact or a soldier falling back never freezes.</summary>
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
            // ⭐ AQ88 C (R-265) — hidden from EVERY known threat outweighs distance + path cost (each ≤ 1); with no such point the
            //   least-exposed wins, so the unit never freezes on open ground.
            ScoreExpensive = new IEqsTest[] { new ThreatExposureTest { Weight = ThreatExposureTest.HideFromAllWeight }, new PathCostScoreTest() },
            MaxCandidates  = 128,
            KeepAllWhenFilteredOut = true,
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
