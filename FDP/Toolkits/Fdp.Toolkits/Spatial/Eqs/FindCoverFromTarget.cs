using System;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Starter EQS template: finds cover positions that provide occlusion from the
    /// primary tracked threat. Composed of CoverPointsGenerator + CheapLineOfSightTest
    /// + DistanceScoreTest.
    ///
    /// BlueprintId is the FNV-1a 32-bit hash of the AssetId GUID below.
    /// </summary>
    [EqsTemplate("f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d")]
    public static class FindCoverFromTarget
    {
        /// <summary>
        /// FNV-1a 32-bit hash of the AssetId GUID "f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d".
        /// Used as the BlueprintId key in IEqsTemplateRegistry.
        /// </summary>
        public const uint BlueprintId = 0x7F3A2B1Cu;

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
