using System.Linq;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// BP-109 -- the end-to-end smoke test.
/// <para>
/// Three assets, loaded from the SAME <c>.bp.json</c> recipe files a designer opens in the editor
/// (<c>Hrot.AI.Behaviors/Recipes/Blueprints/Smoke{MathLib,Patrol,Guard}.bp.json</c>):
/// </para>
/// <list type="bullet">
/// <item><b>SmokeMathLib</b> (Library) -- exports one function, <c>Combine(A,B) = A + B</c>.</item>
/// <item><b>SmokePatrol</b> (Instance) -- a local "ComputeLocal" Function graph returning
/// <c>PatrolSpeed * 3</c> (derived from ITS OWN state), plus a Tick graph that also calls
/// SmokeMathLib's <c>Combine</c> peer function.</item>
/// <item><b>SmokeGuard</b> (Instance) -- the same shape as SmokePatrol, but seeded with a
/// DIFFERENT state variable (<c>GuardSpeed</c>), proving per-entity isolation.</item>
/// </list>
/// <para>
/// This is also the first test in the suite to attach TWO DIFFERENT Instance blueprints to TWO
/// DIFFERENT entities in the SAME <see cref="BlueprintTestFixture"/> world and tick them together --
/// every existing <c>BlueprintRunHarness</c> caller does one entity + one asset, so that shape was
/// unproven before this test.
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class BP109_SmokeTestEndToEndTests
{
    // ---- Recipe loading (mirrors RecipeIntegrityTests.LoadRecipe) ----------

    /// <summary>
    /// Loads a recipe by name, preferring the production location
    /// (<c>Hrot.AI.Behaviors</c> assembly output's <c>Recipes/Blueprints/</c>, which is where the
    /// three BP-109 <c>.bp.json</c> files actually live) and falling back to the test-project
    /// <c>TestAssets/Recipes/</c> copy for isolated test-filter runs that never load that assembly.
    /// </summary>
    private static BlueprintAsset LoadRecipe(string name)
    {
        // Force-load Hrot.AI.Behaviors (mirrors DiscoverRecipesTests) -- an isolated single-test
        // run (e.g. `dotnet test --filter`) may never otherwise touch a type from that assembly,
        // so AppDomain.CurrentDomain.GetAssemblies() would not see it yet even though this test
        // project references it.
        try { System.Reflection.Assembly.Load("Hrot.AI.Behaviors"); } catch { /* best-effort */ }

        var aiBehaviorsAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors");

        string path;
        if (aiBehaviorsAssembly != null)
        {
            var assemblyLocation = Path.GetDirectoryName(aiBehaviorsAssembly.Location)
                ?? throw new InvalidOperationException("Cannot determine Hrot.AI.Behaviors assembly location.");
            path = Path.Combine(assemblyLocation, "Recipes", "Blueprints", name + ".bp.json");
        }
        else
        {
            var dir = TestData.ResolveTestAssetsDir();
            path = Path.Combine(dir, "Recipes", name + ".bp.json");
        }

        if (!File.Exists(path))
            throw new FileNotFoundException($"Recipe file not found: {path}");

        var json = File.ReadAllText(path);
        return BlueprintJsonServices.Deserialize(json)
            ?? throw new InvalidDataException($"Null from '{path}'");
    }

    /// <summary>
    /// Builds the <see cref="BlueprintSignature"/> for the loaded SmokeMathLib asset so
    /// SmokePatrol/SmokeGuard's <c>CallPeerBlueprint</c> nodes can resolve it as a compiled sibling.
    /// Mirrors <c>RecipeIntegrityTests.MakeSquadStateSignature</c>, except <see cref="BlueprintSignature.BlueprintId"/>
    /// is the REAL <see cref="BlueprintIdHash.Compute"/> hash (not the placeholder 0 that
    /// Stage2-validation-only caller uses) -- a real Roslyn compile-and-run needs the hash to match
    /// what <c>StatementEmitter.ResolveSiblingClassName</c> looks up.
    /// </summary>
    private static BlueprintSignature MakeMathLibSignature(BlueprintAsset mathLib) => new(
        Path:              "",
        AssetId:           mathLib.AssetId,
        Name:              mathLib.Name,
        SanitizedName:     mathLib.Name,
        BlueprintId:       BlueprintIdHash.Compute(mathLib.AssetId),
        Dispatch:          mathLib.Dispatch,
        ExportedFunctions: mathLib.Graphs
            .Where(g => g.Kind == GraphKind.Function)
            .Select(g => new BlueprintFunctionSig(
                g.Name,
                g.Inputs.Select(p => new BlueprintParamSig(p.Name, p.Type.TypeId)).ToArray(),
                g.Outputs.Select(p => new BlueprintParamSig(p.Name, p.Type.TypeId)).ToArray()))
            .ToArray(),
        Hostings:              Array.Empty<AiPrimitiveHosting>(),
        DeclaredCallablePeers: mathLib.CallablePeers.ToArray());

    private static CompileOptions MakeOptions(BlueprintSignature mathLibSignature) => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: new[] { mathLibSignature });

    // =========================================================================
    // BP-109: two different Instance blueprints, sharing one Library peer,
    // attached to two different entities in the SAME world, ticked together.
    // =========================================================================

    [Fact]
    public void TwoEntities_TwoDifferentBlueprints_OneSharedLibraryPeer_EndToEnd()
    {
        var mathLib = LoadRecipe("SmokeMathLib");
        var patrol  = LoadRecipe("SmokePatrol");
        var guard   = LoadRecipe("SmokeGuard");

        var options = MakeOptions(MakeMathLibSignature(mathLib));

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Compile and load all three together (merged into one Roslyn compilation), exactly like
        // production's Hrot.AI.Behaviors build compiles all sibling blueprints together.
        fixture.CompileAndLoadMany(new[] { mathLib, patrol, guard }, options);

        // ---- Two entities, two DIFFERENT Instance blueprints, one world ----
        var patrolEntity = fixture.CreateEntity();
        fixture.AttachBlueprint(patrol, patrolEntity);

        var guardEntity = fixture.CreateEntity();
        fixture.AttachBlueprint(guard, guardEntity);

        // One shared tick pumps BOTH entities through the SAME BlueprintTickSystem.
        fixture.TickFrame(0.016f);

        var patrolState = fixture.GetBlueprintState(patrol, patrolEntity);
        var guardState  = fixture.GetBlueprintState(guard, guardEntity);
        Assert.NotNull(patrolState);
        Assert.NotNull(guardState);

        Assert.True(patrolState!.Value.TryGetField<int>("LocalResult", out var patrolLocal),
            "SmokePatrol has no int field 'LocalResult'.");
        Assert.True(guardState!.Value.TryGetField<int>("LocalResult", out var guardLocal),
            "SmokeGuard has no int field 'LocalResult'.");
        Assert.True(patrolState!.Value.TryGetField<int>("LibResult", out var patrolLib),
            "SmokePatrol has no int field 'LibResult'.");
        Assert.True(guardState!.Value.TryGetField<int>("LibResult", out var guardLib),
            "SmokeGuard has no int field 'LibResult'.");

        // ---- Assertion 1: per-entity isolation ----
        // SmokePatrol.ComputeLocal = PatrolSpeed(5) * 3 = 15.
        // SmokeGuard.ComputeLocal  = GuardSpeed(7)  * 3 = 21.
        // Different seeds -> different exact values -- proves each entity's local Function graph
        // read its OWN state, not a shared/aliased slot.
        Assert.Equal(15, patrolLocal);
        Assert.Equal(21, guardLocal);

        // ---- Assertion 2 (+ 3, starred): shared library consistency AND the peer call actually ran ----
        // Both Tick graphs call the SAME SmokeMathLib.Combine(10, 20) with the SAME literal
        // arguments -- the peer call must have actually executed and returned 30 in BOTH cases
        // (not merely "the graph ticked"), and the two independent calls must agree.
        Assert.Equal(30, patrolLib);
        Assert.Equal(30, guardLib);
        Assert.Equal(patrolLib, guardLib);
    }
}
