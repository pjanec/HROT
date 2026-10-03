using System;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Starter EQS template: finds cover positions that provide occlusion from the
    /// primary tracked threat. Composed of CoverPointsGenerator + CheapLineOfSightTest
    /// + DistanceScoreTest.
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
        /// ⛔ It was a hand-typed <c>0x7F3A2B1C</c> whose comment claimed to be this hash and matched no hash at all.
        /// </summary>
        public const uint BlueprintId = 0x082E6DADu;

        /// <summary>
        /// Builds the compiled template. Static and pure: no runtime state read.
        /// </summary>
        /// <param name="los">LOS service (inject BlockedLosService for Phase 3 stub).</param>
        public static EqsQueryTemplate Build(ILosService los)
        {
            return new EqsQueryTemplate
            {
                BlueprintId   = BlueprintId,
                Generator     = new CoverPointsGenerator(),
                FilterCheap   = new IEqsTest[] { new CheapLineOfSightTest(los) },
                ScoreCheap    = new IEqsTest[] { new DistanceScoreTest() },
                MaxCandidates = 32,
            };
        }

        /// <summary>
        /// Overload for the Roslyn source generator. Uses BlockedLosService so no runtime
        /// dependencies are required. The returned template is used only for StructureHash
        /// computation, not for live evaluation.
        /// </summary>
        public static EqsQueryTemplate Build(IEqsTemplateBuilder b)
            => Build(new BlockedLosService());
    }
}
