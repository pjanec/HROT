using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Editor.AiShared;
using Hrot.Hsm.Editor;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Hrot.Hsm.Editor.Validation;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

/// <summary>
/// Tests for the HsmShowcase.hsm.json showcase machine and the Starter recipe.
/// BATCH-HS-07: verifies deserialization, byte-stable round-trip, validation, and shape.
/// </summary>
public sealed class HsmShowcaseTests
{
    // ── helpers ─────────────────────────────────────────────────────────────────

    private static string RepoRoot
    {
        get
        {
            var asmDir = Path.GetDirectoryName(typeof(HsmShowcaseTests).Assembly.Location)!;
            var dir = asmDir;
            for (int i = 0; i < 7; i++)
                dir = Path.GetDirectoryName(dir)!;
            return dir;
        }
    }

    private static string ShowcaseJsonPath =>
        Path.Combine(RepoRoot, "Hrot", "Subsystems", "Hrot.AI.Behaviors",
            "Assets", "HSMs", "HsmShowcase.hsm.json");

    private static string ReadShowcaseJson()
    {
        File.Exists(ShowcaseJsonPath).Should().BeTrue(
            $"Showcase JSON must exist at {ShowcaseJsonPath}");
        return File.ReadAllText(ShowcaseJsonPath);
    }

    private static HsmAsset LoadShowcaseModel()
    {
        var json = ReadShowcaseJson();
        var dto = HsmJsonServices.Deserialize(json);
        dto.Should().NotBeNull("Showcase JSON must deserialize to a non-null DTO");
        return HsmAssetMapper.ToModel(dto!, sourceFilePath: ShowcaseJsonPath, isEditorOwned: true);
    }

    // ── Showcase deserialization ────────────────────────────────────────────────

    [Fact]
    public void Showcase_Deserializes_To_NonNull_Dto()
    {
        var json = ReadShowcaseJson();
        var dto = HsmJsonServices.Deserialize(json);
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("HsmShowcase");
        dto.States.Should().NotBeEmpty();
    }

    [Fact]
    public void Showcase_Deserializes_To_Valid_Model()
    {
        var asset = LoadShowcaseModel();
        asset.Should().NotBeNull();
        asset.Name.Should().Be("HsmShowcase");
        asset.AllStates.Should().NotBeEmpty();
    }

    // ── Showcase round-trip byte-stability ──────────────────────────────────────

    [Fact]
    public void Showcase_RoundTrip_Is_ByteStable()
    {
        var json = ReadShowcaseJson();

        // First round-trip: deserialize → serialize.
        var dto1 = HsmJsonServices.Deserialize(json);
        dto1.Should().NotBeNull();
        var ser1 = HsmJsonServices.Serialize(dto1!);

        // Second round-trip: deserialize → serialize again.
        var dto2 = HsmJsonServices.Deserialize(ser1);
        dto2.Should().NotBeNull();
        var ser2 = HsmJsonServices.Serialize(dto2!);

        // Byte-stable: the two serialized forms must be identical.
        ser2.Should().Be(ser1, "serialize(deserialize(x)) must be byte-stable");
    }

    [Fact]
    public void Showcase_TripleRoundTrip_Stable()
    {
        var json = ReadShowcaseJson();

        var dto1 = HsmJsonServices.Deserialize(json);
        var ser1 = HsmJsonServices.Serialize(dto1!);

        var dto2 = HsmJsonServices.Deserialize(ser1);
        var ser2 = HsmJsonServices.Serialize(dto2!);

        var dto3 = HsmJsonServices.Deserialize(ser2);
        var ser3 = HsmJsonServices.Serialize(dto3!);

        ser3.Should().Be(ser1, "serialize must be stable across multiple round-trips");
    }

    // ── Showcase validation ─────────────────────────────────────────────────────

    [Fact]
    public void Showcase_Validates_With_Zero_Errors()
    {
        var asset = LoadShowcaseModel();
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        var errors = diagnostics.Where(d => d.Severity == HsmDiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            $"Showcase must have 0 Error-severity diagnostics, but got: {string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"))}");

        // Warnings are acceptable — log them for visibility.
        var warnings = diagnostics.Where(d => d.Severity == HsmDiagnosticSeverity.Warning).ToList();
        if (warnings.Count > 0)
        {
            // Warnings are expected (e.g. OutputLaneConflict from parallel regions).
            // Document them but don't fail.
            System.Diagnostics.Debug.WriteLine(
                $"Showcase validation warnings ({warnings.Count}): {string.Join("; ", warnings.Select(w => $"{w.Code}: {w.Message}"))}");
        }
    }

    // ── Showcase shape assertions ───────────────────────────────────────────────

    [Fact]
    public void Showcase_Has_ParallelState_With_AtLeast_Two_Regions()
    {
        var asset = LoadShowcaseModel();

        var parallelStates = asset.AllStates.Where(s => s.IsParallel).ToList();
        parallelStates.Should().NotBeEmpty("Showcase must contain at least one parallel state");

        // Regions exist on AllRegions; the mapping from region → parallel state is implicit
        // via child state RegionIndex assignment.  Verify ≥2 regions exist with distinct
        // RegionIndex values belonging to the parallel state's children.
        var parallel = parallelStates.First();
        var regionIndices = parallel.Children
            .Select(c => c.RegionIndex)
            .Distinct()
            .ToList();
        regionIndices.Should().HaveCountGreaterOrEqualTo(2,
            $"Parallel state '{parallel.Name}' must have children in ≥2 distinct region indices");

        // Also verify the Regions DTO entries exist in AllRegions.
        var regionCount = asset.AllRegions.Count(r => r.RegionIndex >= 0);
        regionCount.Should().BeGreaterOrEqualTo(2,
            "Showcase must define ≥2 regions in AllRegions");
    }

    [Fact]
    public void Showcase_Has_HistoryPseudoState_Inside_Composite()
    {
        var asset = LoadShowcaseModel();

        var historyStates = asset.AllStates.Where(s => s.IsHistory || s.IsDeepHistory).ToList();
        historyStates.Should().NotBeEmpty("Showcase must contain at least one history pseudo-state");

        foreach (var h in historyStates)
        {
            h.Parent.Should().NotBeNull("History pseudo-state must have a parent");
            h.Parent!.Children.Should().HaveCountGreaterThan(1,
                "History pseudo-state must be inside a composite with multiple children");
            // Must not be a direct child of the synthetic root (HistoryOutsideComposite guard).
            h.Parent!.Parent.Should().NotBeNull("History pseudo-state must not be at root level");
        }
    }

    [Fact]
    public void Showcase_Has_FinalState_With_No_Children_And_No_Outgoing_Transitions()
    {
        var asset = LoadShowcaseModel();

        var finalStates = asset.AllStates.Where(s => s.IsFinal).ToList();
        finalStates.Should().NotBeEmpty("Showcase must contain at least one final state");

        foreach (var f in finalStates)
        {
            f.Children.Should().BeEmpty("Final state must have no children");
            f.OutgoingTransitions.Should().BeEmpty("Final state must have no outgoing transitions");
        }
    }

    [Fact]
    public void Showcase_Has_AtLeast_Two_Events()
    {
        var asset = LoadShowcaseModel();
        asset.AllEvents.Should().HaveCountGreaterOrEqualTo(2,
            "Showcase must define ≥2 events");
    }

    [Fact]
    public void Showcase_Has_AtLeast_One_GlobalTransition()
    {
        var asset = LoadShowcaseModel();
        asset.AllGlobalTransitions.Should().NotBeEmpty(
            "Showcase must contain ≥1 global transition");
    }

    [Fact]
    public void Showcase_Has_Composite_With_Initial_Child()
    {
        var asset = LoadShowcaseModel();

        // Find non-parallel composites (states with children) and verify each has exactly 1 initial child.
        // Parallel states have one initial child per region (handled by Region.InitialChildStableId);
        // the validator only checks non-parallel composites.
        var composites = asset.AllStates
            .Where(s => s.Children.Count > 0 && !s.IsParallel)
            .ToList();
        composites.Should().NotBeEmpty("Showcase must have at least one non-parallel composite state");

        foreach (var c in composites)
        {
            var initialCount = c.Children.Count(ch => ch.IsInitial);
            initialCount.Should().Be(1,
                $"Composite '{c.Name}' must have exactly 1 initial child, but has {initialCount}");
        }
    }

    [Fact]
    public void Showcase_All_Transitions_Have_Null_GuardFunction()
    {
        var asset = LoadShowcaseModel();

        foreach (var t in asset.AllTransitions)
        {
            t.GuardFunction.Should().BeNull(
                $"Transition {t.VisualId} must have GuardFunction=null (VE-DEBT-004)");
        }
        foreach (var g in asset.AllGlobalTransitions)
        {
            g.GuardFunction.Should().BeNull(
                $"Global transition {g.VisualId} must have GuardFunction=null (VE-DEBT-004)");
        }
    }

    [Fact]
    public void Showcase_Has_AtLeast_One_Transition_With_StubIdle_Action()
    {
        var asset = LoadShowcaseModel();

        var stubIdleFqn = "Hrot.AI.Behaviors.CgfHsmNodes.StubIdle";

        var transitionsWithAction = asset.AllTransitions
            .Where(t => t.ActionFunction == stubIdleFqn).ToList();
        transitionsWithAction.Should().NotBeEmpty(
            $"Showcase must have ≥1 transition with ActionFunction bound to {stubIdleFqn}");
    }

    [Fact]
    public void Showcase_Has_At_Least_One_State_Bound_To_StubIdle()
    {
        var asset = LoadShowcaseModel();

        var stubIdleFqn = "Hrot.AI.Behaviors.CgfHsmNodes.StubIdle";

        var statesWithStubIdle = asset.AllStates.Where(s =>
            s.OnEntryAction == stubIdleFqn ||
            s.OnExitAction == stubIdleFqn ||
            s.ActivityAction == stubIdleFqn ||
            s.TimerAction == stubIdleFqn).ToList();

        statesWithStubIdle.Should().NotBeEmpty(
            $"Showcase must have ≥1 state with an action bound to {stubIdleFqn}");
    }

    [Fact]
    public void Showcase_Transitions_EventNames_Reference_Defined_Events()
    {
        var asset = LoadShowcaseModel();

        var eventNames = asset.AllEvents.Select(e => e.Name).ToHashSet();

        foreach (var t in asset.AllTransitions)
        {
            if (!string.IsNullOrEmpty(t.EventName))
            {
                eventNames.Should().Contain(t.EventName!,
                    $"Transition {t.VisualId} references event '{t.EventName}' which must be defined");
            }
        }
        foreach (var g in asset.AllGlobalTransitions)
        {
            if (!string.IsNullOrEmpty(g.EventName))
            {
                eventNames.Should().Contain(g.EventName!,
                    $"Global transition {g.VisualId} references event '{g.EventName}' which must be defined");
            }
        }
    }

    // ── Starter recipe ──────────────────────────────────────────────────────────

    [Fact]
    public void StarterRecipe_Is_In_AvailableRecipes()
    {
        using var tempDir = new TempDirectory();
        var svc = new HsmNewAssetService(tempDir.Path);
        var recipes = svc.AvailableRecipes();

        recipes.Should().Contain(r => r.Name == "Starter",
            "AvailableRecipes must include the 'Starter' recipe");
        var starter = recipes.First(r => r.Name == "Starter");
        starter.Kind.Should().Be(AssetKind.Hsm);
    }

    [Fact]
    public void StarterRecipe_EmptyRecipe_Still_In_AvailableRecipes()
    {
        using var tempDir = new TempDirectory();
        var svc = new HsmNewAssetService(tempDir.Path);
        var recipes = svc.AvailableRecipes();

        recipes.Should().Contain(r => r.Name == "Empty",
            "AvailableRecipes must still include the 'Empty' recipe");
    }

    [Fact]
    public void StarterRecipe_Deserializes_To_Valid_Dto()
    {
        var dto = HsmNewAssetService.MakeStarterDto();
        dto.Should().NotBeNull();
        dto.Name.Should().Be("Starter");
        dto.States.Should().NotBeEmpty();
        dto.Regions.Should().NotBeEmpty();
    }

    [Fact]
    public void StarterRecipe_Has_Exactly_One_Initial_State()
    {
        var dto = HsmNewAssetService.MakeStarterDto();

        var initialStates = dto.States.Where(s => s.IsInitial).ToList();
        initialStates.Should().HaveCount(1,
            "Starter recipe must have exactly one Initial state");
    }

    [Fact]
    public void StarterRecipe_RoundTrips()
    {
        var dto = HsmNewAssetService.MakeStarterDto();
        var json = HsmJsonServices.Serialize(dto);
        var dto2 = HsmJsonServices.Deserialize(json);
        dto2.Should().NotBeNull();
        dto2!.Name.Should().Be("Starter");
        dto2.States.Should().HaveCount(dto.States.Count);
    }

    [Fact]
    public void StarterRecipe_Validates_With_Zero_Errors()
    {
        var dto = HsmNewAssetService.MakeStarterDto();
        var asset = HsmAssetMapper.ToModel(dto, sourceFilePath: "", isEditorOwned: true);
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);
        var errors = diagnostics.Where(d => d.Severity == HsmDiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            $"Starter recipe must validate with 0 Errors, but got: {string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"))}");
    }

    [Fact]
    public void StarterRecipe_CanBeCloned_Via_Service()
    {
        using var tempDir = new TempDirectory();
        var svc = new HsmNewAssetService(tempDir.Path);
        var starter = svc.AvailableRecipes().First(r => r.Name == "Starter");

        var result = svc.CreateNew(starter, "MyNewHsm", "");
        result.Should().NotBeNull();
        result.Kind.Should().Be(AssetKind.Hsm);
        result.Name.Should().Be("MyNewHsm");

        // The written file must exist and deserialize.
        var filePath = result.SourceFilePath;
        File.Exists(filePath).Should().BeTrue();
        var json = File.ReadAllText(filePath);
        var dto = HsmJsonServices.Deserialize(json);
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("MyNewHsm");
        dto.States.Should().NotBeEmpty();

        // Must have a fresh AssetId different from the recipe.
        var starterDto = HsmNewAssetService.MakeStarterDto();
        dto.AssetId.Should().NotBe(starterDto.AssetId);
    }

    // ── disposable temp directory helper ────────────────────────────────────────

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "HsmShowcaseTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
