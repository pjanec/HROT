using System.Reflection;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Ensures every emittable diagnostic code has at least one test method
/// annotated with [CoversDiagnosticCode]. This acts as a coverage ratchet.
/// </summary>
public sealed class V_AllValidatorsCoverageTests
{
    // Codes that are defined but not yet emitted by current implementation.
    private static readonly HashSet<string> KnownNotYetEmittedCodes = new(StringComparer.Ordinal)
    {
        "BP1600",  // OrphanedNode: declared as graph-structure code, not yet emitted
        "BP2015",  // WhenNode downstream of Branch: deferred (pins not materialized at Stage 2)
        "BP3012",  // Reserved for Stage 3, future use
        "BP3001",  // Reserved for Stage 4, Slice 2
        "BP4002",  // Reserved for Stage 5, Slice 2
        "BP4003",  // Reserved for Stage 5, Slice 2
        "BP1413",  // LatentInSequence: safety valve; fall-through propagation handles it, not emitted
        "BP6001",  // Reserved for Stage 7, Slice 2
    };

    [Fact]
    public void AllDiagnosticCodes_HaveAtLeastOneTestCovering()
    {
        var allDefinedCodes = GetAllDefinedCodes();
        var coveredCodes    = TestDiagnosticInventory.GetCoveredCodes();

        var missing = allDefinedCodes
            .Except(KnownNotYetEmittedCodes)
            .Except(coveredCodes)
            .OrderBy(c => c)
            .ToList();

        Assert.True(missing.Count == 0,
            $"The following diagnostic codes have no test coverage:\n  {string.Join("\n  ", missing)}\n"
            + "Add a [CoversDiagnosticCode(\"BPXXXX\")] attribute to at least one test per code.");
    }

    [Fact]
    public void KnownNotYetEmittedCodes_NoneAreActuallyCovered()
    {
        // Fail loudly when a code that was 'not yet emitted' gets covered --
        // the developer should remove it from KnownNotYetEmittedCodes.
        var covered    = TestDiagnosticInventory.GetCoveredCodes();
        var alreadyCovered = KnownNotYetEmittedCodes.Intersect(covered).OrderBy(c => c).ToList();

        Assert.True(alreadyCovered.Count == 0,
            $"The following codes are in KnownNotYetEmittedCodes but ARE covered by tests.\n"
            + $"Remove them from KnownNotYetEmittedCodes:\n  {string.Join("\n  ", alreadyCovered)}");
    }

    // ---- Helpers --------------------------------------------------------

    private static IReadOnlyList<string> GetAllDefinedCodes()
    {
        var codes = new List<string>();
        foreach (var field in typeof(DiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType == typeof(string) && field.GetValue(null) is string code)
                codes.Add(code);
        }
        // De-duplicate (e.g. BP5001 is defined twice with different aliases).
        return codes.Distinct().ToList();
    }
}
