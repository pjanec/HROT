using System;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ §6.6 starter template: cover from the target in context slot 1 (docs/designs/eqs-2/EQS_Design_v1.3_final.md §19.6).
    /// Cover points (the terrain's, <see cref="TerrainCoverProvider"/>) near the self → kept only where the target CANNOT see
    /// the self's crouched eye → nearer scores higher, plus how hidden it is from every known threat, and the path cost to it
    /// (unreachable points drop out).
    /// <para>⛔ SUPERSEDED (§19.1 H1): the registry built it over <c>BlockedLosService</c>, so no point was ever rejected.</para>
    /// </summary>
    [EqsTemplate(AssetId)]
    public static class FindCoverFromTarget
    {
        /// <summary>The template's asset identity.</summary>
        public const string AssetId = "f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d";

        /// <summary>
        /// ⭐ <c>CE-2034</c> — FNV-1a over <see cref="AssetId"/>'s 16 bytes, the id <see cref="EqsTemplateRegistry"/> keys
        /// the template by and a blueprint <c>SpawnEqsSensor</c> bakes. A rail pins it to
        /// <see cref="EqsTemplateRegistry.BlueprintIdOf"/>(<see cref="AssetId"/>).
        /// </summary>
        public const uint BlueprintId = 0x082E6DADu;

        /// <summary>The production template: sight over the resident terrain. Static and pure (§5.6).</summary>
        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => Build(los: null);

        /// <summary>The same template over an explicit sight source (tests; a host-specific one such as Stride's raycasts).</summary>
        public static EqsQueryTemplate Build(ILosService? los) => new EqsQueryTemplate
        {
            BlueprintId    = BlueprintId,
            Generator      = new CoverPointsGenerator(),
            FilterCheap    = new IEqsTest[] { los == null ? new CheapLineOfSightTest() : new CheapLineOfSightTest(los) },
            ScoreCheap     = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive = new IEqsTest[]
            {
                los == null ? new ThreatExposureTest() : new ThreatExposureTest(los),
                new PathCostScoreTest(),
            },
            MaxCandidates  = 32,
        };
    }
}
