using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Navigation;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// P1b (GAP-1) proof for the Hill-attack -&gt; Blueprints migration, slice 4
/// (<c>docs/blueprints/P1_FlowForEach_Design.md</c> path B, and
/// <c>docs/blueprints/HillAssault_Blueprint_Migration.md</c> slice 4). Rebuilds the C# oracle
/// <c>HillAttackCommanderNodes.Condition_AreAllAtBaseline</c> as a visually-authored AiPrimitive
/// BTreeCondition (<c>Assets/Blueprints/HillAssault2_AreAllAtBaseline.bp.json</c>), exercising the
/// NEW in-body <c>Branch</c> -&gt; inline <c>if</c>/<c>else</c> scheduler path (P1b) together with
/// P2 (foreign-entity component read via a <c>Target</c>-pinned <c>GetComponent</c>) and P1a
/// (<c>FlowForEach</c> loop).
///
/// <para>
/// Graph: init WorkingState <c>bool AllAtBaseline = true</c> (Literal -&gt; SetVariable), then
/// <c>FlowForEach</c> over the commander's <c>UnitRoster</c> (curated
/// <c>UnitRosterOps.Count</c>/<c>Subordinate</c>) whose Body reads each subordinate's
/// <c>NavigationStatus.Result</c> via <c>GetComponent</c>(Target=CurrentItem) and, through the
/// GAP-12 stopgap comparator <c>HillAssault2NavOps.IsArrived</c>, BRANCHES: <c>Branch.True</c>
/// (arrived) is unwired (do nothing); <c>Branch.False</c> (not arrived) sets
/// <c>AllAtBaseline = false</c> -- i.e. the AND-reduce
/// <c>if (!IsArrived(nav.Result)) AllAtBaseline = false;</c> emitted as a NESTED inline
/// <c>if</c>/<c>else</c> INSIDE the <c>for</c> loop (<c>IrOp_If</c>), NOT a BFS block split. After the
/// loop, <c>GetVariable(AllAtBaseline)</c> -&gt; <c>Branch</c> returns Success/Failure.
/// </para>
///
/// <para>
/// Compiled by the REAL Roslyn source generator as part of <c>Hrot.AI.Behaviors</c>'s own build. Does
/// not modify the C# oracle. GAP-12: the enum-equality check lives in the tiny pure <c>IsArrived</c>
/// helper until a native Compare node lands; the loop + in-body branch (the reusable capability) are
/// proven natively now.
/// </para>
/// </summary>
public sealed class HillAssault2_AreAllAtBaseline_ProofTests
{
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2AreAllAtBaseline_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_AreAllAtBaseline.bp.json must compile via the real Roslyn source generator " +
            "into a Hrot.AI.Behaviors.Generated.HillAssault2AreAllAtBaseline_*_Bp class");
        return type!;
    }

    private static string FindGeneratedSourceText()
    {
        var generatedDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Hrot.AI.Behaviors",
            "obj", "GeneratedFiles", "Hrot.Blueprints.Generators",
            "Hrot.Blueprints.Generators.BlueprintIncrementalGenerator");

        var file = Directory.Exists(generatedDir)
            ? Directory.GetFiles(generatedDir, "HillAssault2AreAllAtBaseline_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2_AreAllAtBaseline must exist under {generatedDir}");
        return File.ReadAllText(file!);
    }

    /// <summary>Invokes the generated <c>TickCore</c> once via reflection. WorkingState is re-initialized
    /// to <c>AllAtBaseline = true</c> at graph entry, so a fresh (zeroed) struct here is irrelevant.</summary>
    private static Fbt.NodeStatus TickOnce(Type bpType, Entity entity, EntityRepository world)
    {
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        object?[] args =
        {
            Activator.CreateInstance(paramsType),
            Activator.CreateInstance(wsType),
            entity,
            world,
            0f,
        };
        var result = tickCore!.Invoke(null, args);
        return (Fbt.NodeStatus)result!;
    }

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<UnitRoster>();
        world.RegisterComponent<NavigationStatus>();
        return world;
    }

    private static Entity AddSubordinate(EntityRepository world, ref UnitRoster roster, NavigationResult result)
    {
        var sub = world.CreateEntity();
        world.AddComponent(sub, new NavigationStatus { Result = result });
        UnitRoster.Add(ref roster, (long)sub.PackedValue);
        return sub;
    }

    // ── Source-inspection: the in-body branch lowers to a nested inline if/else (P1b) ─────────

    [Fact]
    public void GeneratedTickCore_SourceContainsInlineIfElseInsideForLoop_OverPerSubordinateComponentRead()
    {
        // Ensure the type actually built via the real generator before inspecting its source.
        FindGeneratedBlueprintType();
        var source = FindGeneratedSourceText();

        source.Should().Contain("for (",
            "the FlowForEach must lower to an inline C# for loop (P1a) -- see generated TickCore below:\n" + source);
        source.Should().Contain("UnitRosterOps.Count(",
            "the loop bound must call the curated UnitRosterOps.Count accessor -- see below:\n" + source);
        source.Should().Contain("UnitRosterOps.Subordinate(",
            "the per-iteration item must be read via the curated UnitRosterOps.Subordinate accessor -- see below:\n" + source);
        source.Should().Contain(
            "GetComponentRO<global::Fdp.Toolkit.Navigation.NavigationStatus>",
            "each subordinate's NavigationStatus must be read via a reflection-free, Target-pinned " +
            "GetComponentRO<global::FQN> (P2 foreign-entity read) -- see below:\n" + source);
        source.Should().Contain("HillAssault2NavOps.IsArrived(",
            "the GAP-12 stopgap comparator must turn the field read into the branch condition -- see below:\n" + source);

        // P1b core evidence: the in-body Branch emits as a nested inline `if`/`else` (IrOp_If), NOT a
        // BFS block split -- and it mutates the WorkingState AND-reduce accumulator on the False arm.
        source.Should().Contain("if (",
            "the in-body Branch must lower to a nested inline `if` -- see generated TickCore below:\n" + source);
        source.Should().Contain("else",
            "the in-body Branch's False arm must emit as the `else` block of the inline if/else -- see below:\n" + source);
        // The not-arrived (Branch.False) arm assigns a `false` literal into the AND-reduce accumulator.
        // (The literal is emitted into a temp first: `var __tN = false; ws.AllAtBaseline = __tN;`.)
        source.Should().Contain("= false;",
            "the not-arrived arm must assign a false literal -- see generated TickCore below:\n" + source);

        // The reduce write into the accumulator must be nested inside the for loop, not scheduled as a
        // separate block. The init write is `ws.AllAtBaseline = __t0;` BEFORE the loop; the in-loop
        // reduce write is the LAST `ws.AllAtBaseline = ` assignment (the post-loop read is
        // `= ws.AllAtBaseline`, which does not match this write prefix).
        int forIdx = source.IndexOf("for (", StringComparison.Ordinal);
        int reduceWriteIdx = source.LastIndexOf("ws.AllAtBaseline = ", StringComparison.Ordinal);
        forIdx.Should().BeGreaterThan(-1);
        reduceWriteIdx.Should().BeGreaterThan(forIdx,
            "the AND-reduce write must appear AFTER the `for (` header -- i.e. nested in the loop body " +
            "(P1b inline if/else), not hoisted into a BFS block -- see generated TickCore below:\n" + source);
        source.Should().NotContain("goto __block_branch_b1000000",
            "the IN-BODY branch (b1...) must NOT lower to a goto/BFS block split -- it must be inline " +
            "if/else -- see generated TickCore below:\n" + source);
    }

    // ── Behavioral parity vs the oracle's arrived/not-arrived decision ───────────────────────

    [Fact]
    public void GeneratedTickCore_AllSubordinatesArrived_ReturnsSuccess()
    {
        var bpType = FindGeneratedBlueprintType();

        using var world = CreateWorld();
        var commander = world.CreateEntity();
        var roster = new UnitRoster();
        AddSubordinate(world, ref roster, NavigationResult.Arrived);
        AddSubordinate(world, ref roster, NavigationResult.Arrived);
        AddSubordinate(world, ref roster, NavigationResult.Arrived);
        world.AddComponent(commander, roster);

        TickOnce(bpType, commander, world).Should().Be(Fbt.NodeStatus.Success,
            "every subordinate's NavigationStatus.Result == Arrived, so the AND-reduce never clears " +
            "AllAtBaseline -> Success");
    }

    [Fact]
    public void GeneratedTickCore_OneSubordinateNotArrived_ReturnsFailure()
    {
        var bpType = FindGeneratedBlueprintType();

        using var world = CreateWorld();
        var commander = world.CreateEntity();
        var roster = new UnitRoster();
        AddSubordinate(world, ref roster, NavigationResult.Arrived);
        AddSubordinate(world, ref roster, NavigationResult.InProgress); // still moving -> not arrived
        AddSubordinate(world, ref roster, NavigationResult.Arrived);
        world.AddComponent(commander, roster);

        TickOnce(bpType, commander, world).Should().Be(Fbt.NodeStatus.Failure,
            "one subordinate is InProgress (not Arrived), so the in-body branch's False arm clears " +
            "AllAtBaseline -> Failure");
    }

    [Fact]
    public void GeneratedTickCore_EmptyRoster_ReturnsSuccess()
    {
        var bpType = FindGeneratedBlueprintType();

        using var world = CreateWorld();
        var commander = world.CreateEntity();
        world.AddComponent(commander, new UnitRoster()); // Count == 0

        TickOnce(bpType, commander, world).Should().Be(Fbt.NodeStatus.Success,
            "an empty roster skips the loop body entirely, so AllAtBaseline stays at its init value " +
            "(true) -> Success (vacuous truth, matching the oracle's HasComponent/empty-roster path)");
    }
}
