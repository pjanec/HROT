using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Validation;

/// <summary>
/// Tests for HsmValidator Rule 8: CrossRegionBlackboardConflict.
/// TASK-BB-1f-01.
/// </summary>
public sealed class HsmValidatorBlackboardConflictTests
{
    // ---- Helpers ------------------------------------------------------------

    // Builds a minimal HsmAsset directly (bypassing compiler pipeline).
    // rootState is the synthetic root (not in allStates).
    private static HsmAsset MakeAsset(
        StateNode rootState,
        List<StateNode> allStates,
        List<RegionNode>? allRegions = null,
        List<TransitionNode>? allTransitions = null)
    {
        return new HsmAsset(
            Guid.NewGuid(), "TestAsset", "", false, "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            rootState,
            allStates,
            allTransitions ?? new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            allRegions ?? new List<RegionNode>(),
            new List<EventDefinition>());
    }

    // Builds a parallel composite with two child states in different regions.
    // Returns (asset, parallel, child0, child1) so tests can wire blackboard bindings.
    private static (HsmAsset Asset, StateNode Parallel, StateNode Child0, StateNode Child1)
        MakeParallelAsset()
    {
        var root     = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        var rn0 = new RegionNode("R0") { RegionIndex = 0 };
        var rn1 = new RegionNode("R1") { RegionIndex = 1 };
        parallel.RegionNodes.Add(rn0);
        parallel.RegionNodes.Add(rn1);

        var child0 = new StateNode("C0") { IsInitial = true, RegionIndex = 0, Parent = parallel };
        var child1 = new StateNode("C1") { RegionIndex = 1, Parent = parallel };
        parallel.Children.Add(child0);
        parallel.Children.Add(child1);

        var asset = MakeAsset(root,
            new List<StateNode> { parallel, child0, child1 },
            new List<RegionNode> { rn0, rn1 });

        return (asset, parallel, child0, child1);
    }

    private static BlackboardAliasBinding MakeBinding(Guid requiringAssetId, Guid requiringElementId) =>
        new(
            RequiringAssetId:   requiringAssetId,
            RequiringElementId: requiringElementId,
            RequiringAssetName: "SomeAsset",
            RequiredByPath:     "SomeAsset > Node#1",
            DtoType:            typeof(float));

    // ---- T1: no blackboard -> no conflict -----------------------------------

    [Fact]
    public void Validate_NoBlackboard_ProducesNoConflictDiagnostic()
    {
        var (asset, _, _, _) = MakeParallelAsset();
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset, blackboard: null);

        Assert.DoesNotContain(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
    }

    // ---- T2: two aliases in different regions -> conflict -------------------

    [Fact]
    public void Validate_ParallelRegionWriteSameVariable_ProducesConflict()
    {
        var (asset, parallel, child0, child1) = MakeParallelAsset();

        var assetId = Guid.NewGuid();
        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        // child0 is in region 0, child1 is in region 1
        bb.AddAlias("speed", MakeBinding(assetId, child0.StableId));
        bb.AddAlias("speed", MakeBinding(assetId, child1.StableId));

        var validator = new HsmValidator();
        var diagnostics = validator.Validate(asset, bb);

        var conflict = Assert.Single(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
        Assert.Equal(HsmDiagnosticSeverity.Warning, conflict.Severity);
        Assert.Contains(parallel.StableId, conflict.TargetStableIds);
    }

    // ---- T7: message content -> includes names -----------------------------

    [Fact]
    public void Validate_ConflictDiagnostic_MessageContainsVariableAndCompositeName()
    {
        var (asset, parallel, child0, child1) = MakeParallelAsset();

        var assetId = Guid.NewGuid();
        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        bb.AddAlias("speed", MakeBinding(assetId, child0.StableId));
        bb.AddAlias("speed", MakeBinding(assetId, child1.StableId));

        var validator = new HsmValidator();
        var diagnostics = validator.Validate(asset, bb);

        var conflict = Assert.Single(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
        Assert.Contains("speed", conflict.Message, StringComparison.Ordinal);
        Assert.Contains("Parallel", conflict.Message, StringComparison.Ordinal);
    }

    // ---- T3: different variables, one per region -> no conflict -------------

    [Fact]
    public void Validate_ParallelRegionWriteDifferentVariables_NoConflict()
    {
        var (asset, _, child0, child1) = MakeParallelAsset();

        var assetId = Guid.NewGuid();
        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        bb.AddVariable("health", typeof(int));
        bb.AddAlias("speed",  MakeBinding(assetId, child0.StableId));
        bb.AddAlias("health", MakeBinding(assetId, child1.StableId));

        var validator = new HsmValidator();
        var diagnostics = validator.Validate(asset, bb);

        Assert.DoesNotContain(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
    }

    // ---- T4: two aliases in the SAME region -> no conflict -----------------

    [Fact]
    public void Validate_SameRegionTwoAliases_NoConflict()
    {
        var (asset, _, child0, _) = MakeParallelAsset();

        var assetId = Guid.NewGuid();
        // Add a second child in region 0 to simulate two bindings in the same region.
        var root = asset.RootState.Children[0].Parent!;  // parallel parent
        // Use two different element IDs but both point to child0 (region 0) via same StableId;
        // actually we need two distinct RequiringElementIds both in region 0.
        // child0 is in region 0 -- use it twice via different assetId combos,
        // but since AddAlias deduplicates by (assetId, elementId), use different assetIds.
        var assetIdA = Guid.NewGuid();
        var assetIdB = Guid.NewGuid();
        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        bb.AddAlias("speed", MakeBinding(assetIdA, child0.StableId));
        bb.AddAlias("speed", MakeBinding(assetIdB, child0.StableId));

        var validator = new HsmValidator();
        var diagnostics = validator.Validate(asset, bb);

        Assert.DoesNotContain(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
    }

    // ---- T5: sequential (non-parallel) states -> no conflict ---------------

    [Fact]
    public void Validate_SequentialStates_NoConflict()
    {
        // Build an asset with a plain composite (non-parallel) and two children.
        var root      = new StateNode("__root__");
        var composite = new StateNode("Composite") { Parent = root };
        root.Children.Add(composite);
        var childA = new StateNode("A") { IsInitial = true, Parent = composite };
        var childB = new StateNode("B") { Parent = composite };
        composite.Children.Add(childA);
        composite.Children.Add(childB);

        var asset = MakeAsset(root, new List<StateNode> { composite, childA, childB });

        var assetId = Guid.NewGuid();
        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        bb.AddAlias("speed", MakeBinding(assetId, childA.StableId));
        bb.AddAlias("speed", MakeBinding(assetId, childB.StableId));

        var validator = new HsmValidator();
        var diagnostics = validator.Validate(asset, bb);

        Assert.DoesNotContain(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
    }

    // ---- T6: only one alias -> no conflict ---------------------------------

    [Fact]
    public void Validate_OnlyOneAlias_NoConflict()
    {
        var (asset, _, child0, _) = MakeParallelAsset();

        var assetId = Guid.NewGuid();
        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        bb.AddAlias("speed", MakeBinding(assetId, child0.StableId));

        var validator = new HsmValidator();
        var diagnostics = validator.Validate(asset, bb);

        Assert.DoesNotContain(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
    }
    // ---- 1f-06: Action Schema Filtering Tests ------------------------------

    [Fact]
    public void Validate_ReadOnlyActions_NoConflict()
    {
        var (asset, parallel, child0, child1) = MakeParallelAsset();
        
        child0.ActivityAction = "ReadOnlyAction";
        child1.ActivityAction = "ReadOnlyAction";

        var assetId = Guid.NewGuid();
        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        bb.AddAlias("speed", MakeBinding(assetId, child0.StableId));
        bb.AddAlias("speed", MakeBinding(assetId, child1.StableId));

        var stubSchema = new StubActionSchemaExporter(("ReadOnlyAction", BlackboardAccess.ReadOnly));
        var validator = new HsmValidator(stubSchema);
        var diagnostics = validator.Validate(asset, bb);

        Assert.DoesNotContain(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
    }

    [Fact]
    public void Validate_MixedAccess_OneReadOnlyOneReadWrite_ProducesConflict()
    {
        var (asset, parallel, child0, child1) = MakeParallelAsset();

        child0.ActivityAction = "ReadOnlyAction";
        child1.ActivityAction = "ReadWriteAction";

        var assetId = Guid.NewGuid();
        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        bb.AddAlias("speed", MakeBinding(assetId, child0.StableId));
        bb.AddAlias("speed", MakeBinding(assetId, child1.StableId));

        var stubSchema = new StubActionSchemaExporter(
            ("ReadOnlyAction", BlackboardAccess.ReadOnly),
            ("ReadWriteAction", BlackboardAccess.ReadWrite)
        );
        var validator = new HsmValidator(stubSchema);
        var diagnostics = validator.Validate(asset, bb);

        Assert.Contains(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
    }

    [Fact]
    public void Validate_NullSchema_TreatsAllAsWriters()
    {
        var (asset, parallel, child0, child1) = MakeParallelAsset();

        child0.ActivityAction = "ReadOnlyAction";
        child1.ActivityAction = "ReadOnlyAction";

        var assetId = Guid.NewGuid();
        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        bb.AddAlias("speed", MakeBinding(assetId, child0.StableId));
        bb.AddAlias("speed", MakeBinding(assetId, child1.StableId));

        var validator = new HsmValidator(schema: null);
        var diagnostics = validator.Validate(asset, bb);

        var conflict = Assert.Single(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
        Assert.Equal(HsmDiagnosticSeverity.Warning, conflict.Severity);
    }

    [Fact]
    public void Validate_StateWithNoActions_NotAWriter()
    {
        var (asset, parallel, child0, child1) = MakeParallelAsset();

        // child0 has NO actions
        child1.ActivityAction = "ReadOnlyAction";

        var assetId = Guid.NewGuid();
        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        bb.AddAlias("speed", MakeBinding(assetId, child0.StableId));
        bb.AddAlias("speed", MakeBinding(assetId, child1.StableId));

        var stubSchema = new StubActionSchemaExporter(("ReadOnlyAction", BlackboardAccess.ReadOnly));
        var validator = new HsmValidator(stubSchema);
        var diagnostics = validator.Validate(asset, bb);

        Assert.DoesNotContain(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
    }
    // ────────────────────────────────────────────────────────────────────────
    // ⭐⭐⭐ W7c — the LOCALLY-BOUND writer style, which rule 9 could not see
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴🔴 <b>RED before <c>W7c</c>.</b> Two transitions, one in each region of the same parallel
    /// composite, both writing the variable <b>directly</b> via <c>ExpressionTargetField</c> — the
    /// locally-bound style. ⛔ <b>Neither records an alias</b>, so the rule saw <b>nothing</b> and the
    /// panel reported the machine as clean.
    ///
    /// <para>
    /// ⭐ §9.2 says the writer set is <i>"every action method that mutates this variable"</i> — not
    /// "every alias". ⚠ <c>BP-240</c>'s shape a third time: a rule that is green because of what it
    /// happens to look at.
    /// </para>
    /// </summary>
    [Fact]
    public void Validate_TwoRegionsWriteViaExpressionTargetField_ProducesConflict()
    {
        var (asset, parallel) = MakeParallelAssetWithTransitions(
            ("speed", 0), ("speed", 1));

        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));

        var diagnostics = new HsmValidator().Validate(asset, bb);

        var conflict = Assert.Single(
            diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
        Assert.Equal(HsmDiagnosticSeverity.Warning, conflict.Severity);
        Assert.Contains(parallel.StableId, conflict.TargetStableIds);
    }

    /// <summary>
    /// ⭐ <b>The union really is a union:</b> one writer of each style, in different regions, still
    /// conflicts. ⚠ Without this, an implementation that swapped one enumeration for the other rather
    /// than combining them would pass both single-style tests.
    /// </summary>
    [Fact]
    public void Validate_OneAliasWriterAndOneExpressionTargetWriter_ProducesConflict()
    {
        var (asset, parallel, child0, _) = MakeParallelAsset();

        // A transition out of the region-1 child, locally bound to "speed".
        var child1 = asset.AllStates.Single(s => s.Name == "C1");
        var t = new TransitionNode
        {
            VisualId              = Guid.NewGuid(),
            Source                = child1,
            Target                = child1,
            ActionFunction        = "Some.Action",
            ExpressionTargetField = "speed",
        };
        var withTransition = MakeAsset(
            RootOf(parallel), new List<StateNode> { parallel, child0, child1 },
            new List<RegionNode>(parallel.RegionNodes),
            new List<TransitionNode> { t });

        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        bb.AddAlias("speed", MakeBinding(Guid.NewGuid(), child0.StableId));   // region 0, alias style

        var diagnostics = new HsmValidator().Validate(withTransition, bb);

        Assert.Single(diagnostics, d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
    }

    /// <summary>
    /// ⭐ <b>Same region ⇒ no conflict</b>, whichever style. ⚠ The rule is about CONCURRENCY, not about
    /// two writers — an implementation that fired on any two writers would pass the tests above.
    /// </summary>
    [Fact]
    public void Validate_TwoExpressionTargetWritersInTheSameRegion_ProducesNoConflict()
    {
        var (asset, _) = MakeParallelAssetWithTransitions(("speed", 0), ("speed", 0));

        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));

        Assert.DoesNotContain(new HsmValidator().Validate(asset, bb),
            d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
    }

    /// <summary>
    /// ⭐ <b>A different variable is a different conflict.</b> ⚠ Guards against matching on
    /// "has an ExpressionTargetField" rather than on the variable's own name.
    /// </summary>
    [Fact]
    public void Validate_ExpressionTargetWritersOfDifferentVariables_ProduceNoConflict()
    {
        var (asset, _) = MakeParallelAssetWithTransitions(("speed", 0), ("heading", 1));

        var bb = new StubBlackboardAsset();
        bb.AddVariable("speed", typeof(float));
        bb.AddVariable("heading", typeof(float));

        Assert.DoesNotContain(new HsmValidator().Validate(asset, bb),
            d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
    }

    // ---- helpers for the W7c cases -----------------------------------------

    private static StateNode RootOf(StateNode parallel) => parallel.Parent!;

    /// <summary>
    /// A parallel composite with one child per region, plus one self-transition per
    /// <c>(variableName, regionIndex)</c> pair, each locally bound via <c>ExpressionTargetField</c>.
    /// </summary>
    private static (HsmAsset Asset, StateNode Parallel) MakeParallelAssetWithTransitions(
        params (string Variable, int Region)[] writers)
    {
        var root     = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        var regions = new List<RegionNode>();
        var states  = new List<StateNode> { parallel };
        var byRegion = new Dictionary<int, StateNode>();

        foreach (int region in writers.Select(w => w.Region).Distinct().OrderBy(r => r))
        {
            var rn = new RegionNode($"R{region}") { RegionIndex = (byte)region };
            parallel.RegionNodes.Add(rn);
            regions.Add(rn);

            var child = new StateNode($"C{region}") { RegionIndex = region, Parent = parallel };
            parallel.Children.Add(child);
            states.Add(child);
            byRegion[region] = child;
        }

        var transitions = writers.Select(w => new TransitionNode
        {
            VisualId              = Guid.NewGuid(),
            Source                = byRegion[w.Region],
            Target                = byRegion[w.Region],
            ActionFunction        = "Some.Action",   // ⚠ unknown FQN ⇒ conservatively a writer (§9.6)
            ExpressionTargetField = w.Variable,
        }).ToList();

        return (MakeAsset(root, states, regions, transitions), parallel);
    }
}

// ---- Stubs ------------------------------------------

file sealed class StubActionSchemaExporter : IActionSchemaExporter
{
    private readonly Dictionary<string, BlackboardAccess> _entries;

    public StubActionSchemaExporter(params (string Fqn, BlackboardAccess Access)[] entries)
    {
        _entries = entries.ToDictionary(e => e.Fqn, e => e.Access);
    }

    public ActionSchemaEntry? Lookup(string fqn)
    {
        if (!_entries.TryGetValue(fqn, out var access)) return null;
        return new ActionSchemaEntry(fqn, typeof(float), ActionHosting.Hsm, access, null);
    }

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => 
        _entries.ToDictionary(kv => kv.Key, kv => 
            new ActionSchemaEntry(kv.Key, typeof(float), ActionHosting.Hsm, kv.Value, null));

    public void Rebuild() { }
    public event Action? Changed { add { } remove { } }
}

file sealed class StubBlackboardAsset : IBlackboardManagedAsset
{
    private readonly List<BlackboardVariableEntry> _vars = new();
    private readonly Dictionary<string, List<BlackboardAliasBinding>> _aliases = new();

    public bool IsBlackboardEditorManaged { get; private set; } = true;
    public void SetBlackboardEditorManaged(bool managed) => IsBlackboardEditorManaged = managed;
    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;

    public void AddVariable(string name, Type type) =>
        _vars.Add(new BlackboardVariableEntry(name, type, null));

    public void AddAlias(string variableName, BlackboardAliasBinding binding)
    {
        if (!_aliases.TryGetValue(variableName, out var list))
        {
            list = new List<BlackboardAliasBinding>();
            _aliases[variableName] = list;
        }
        list.Add(binding);
    }

    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) =>
        _aliases.TryGetValue(variableName, out var list)
            ? list.AsReadOnly()
            : Array.Empty<BlackboardAliasBinding>();

    // ---- Required interface members (unused in these tests) ----------------
    public void AddVariable(BlackboardVariableEntry entry) => _vars.Add(entry);
    public void RemoveVariable(string name) { }
    public void UpdateVariableComment(string name, string? comment) { }
    public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson) { }
    public void MoveVariable(int sourceIndex, int destIndex) { }
    public void RenameVariable(string oldName, string newName) { }
    public int CountNodesReferencingVariable(string name) => 0;
    public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
    public void RemoveVariables(IReadOnlyList<string> names) { }
}
