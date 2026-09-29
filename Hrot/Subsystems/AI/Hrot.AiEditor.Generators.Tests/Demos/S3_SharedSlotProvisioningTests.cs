using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Hrot.AiEditor.Generators;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

// Fully qualify the Roslyn MetadataReferenceResolver to avoid collision.
using RoslynMRR = Hrot.Blueprints.Core.Compiler.Roslyn.MetadataReferenceResolver;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// S3-4 runtime provisioning proof tests (Slice 3 — §4.4 Behavior-scope shared working state).
///
/// These exercise the full emit→generate→compile→register→provision pipeline (mirroring
/// <see cref="T20_MultiStateful_ProofTests"/>), but build the asset DTO IN-MEMORY and serialize
/// it with <see cref="BTreeJsonServices.Serialize"/> so the <c>role</c>/<c>scope</c> enum strings
/// are encoded correctly.
///
/// The single production change under test is in
/// <c>BTreeBridgeEmitCore.EmitStatefulWorkingSlotsArray</c>: the manifest's slot key is now
/// resolved scope-aware (<c>ResolveStatefulSlotKey</c>). When several nodes bind the same
/// <see cref="WorkingStateScope.Behavior"/>-scoped variable, S3-2's Behavior key
/// <c>FNV-1a(assetId, variableId)</c> is identical for all of them, so the existing
/// <c>slotsBySeen</c> dedup collapses them into ONE manifest entry ⇒ ONE provisioned slot.
///
/// The tests do NOT tick — they only assign and count slots. (S3-3 / BATCH-15 reconciles the
/// emitted thunk's baked key; that is out of scope here.)
/// </summary>
public sealed class S3_SharedSlotProvisioningTests : IDisposable
{
    private const string MethodFqn = "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_AdvanceCursor";
    private const string ParamsTypeId = "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorParams";
    private const string WorkingStateTypeId = "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorState";
    // Shared Input param variable projected by every node (State vars no longer pack into the param region).
    private const string ParamVarName = "cfg";

    private readonly BehaviorRegistry  _liveRegistry      = new();
    private readonly BlueprintRegistry _blueprintRegistry = new();

    public void Dispose() => _liveRegistry.Clear();

    // ── World factory (mirrors T20 / BehaviorIngressStatefulTests) ────────────────

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BehaviorState>();
        // ⭐ B4: register from the LADDER, not a hand-list. ⛔ This was three explicit
        //   RegisterComponent calls and it did NOT know about the 256 tier — 11 tests
        //   failed with "Component BlueprintBlackboard256 is not registered" the moment
        //   O3b added one. Production never had the bug: it registers from the table.
        BlueprintTierTable.RegisterAll(world);
        return world;
    }

    // ── DTO builders (in-memory asset) ────────────────────────────────────────────

    // Input param variable (DemoCursorParams) — packed into the param region, projected as param-0.
    private static BlackboardVariableDto ParamVar(string name) => new()
    {
        Name = name,
        Type = new BlackboardTypeRefDto { TypeId = ParamsTypeId },
        DefaultValueJson = "{\"Limit\":1}",
        Role = BlackboardVariableRole.Input,
    };

    // Working-state variable (DemoCursorState) — State role, given scope; NOT packed (lives in the
    // partition tier). Its scope drives the slot key.
    private static BlackboardVariableDto StateVar(string name, WorkingStateScope scope) => new()
    {
        Name = name,
        Type = new BlackboardTypeRefDto { TypeId = WorkingStateTypeId },
        Role = BlackboardVariableRole.State,
        Scope = scope,
    };

    private static BTreeActionNodeDto ActionNode(Guid visualId, string label, string paramField, string stateField) => new()
    {
        VisualId = visualId,
        DisplayLabel = label,
        Action = new BTreeActionPayloadDto
        {
            MethodFqn = MethodFqn,
            ExpressionTargetField = paramField,       // param projection (DemoCursorParams)
            WorkingStateTargetField = stateField,     // working-state variable — drives scope/key
            DelegateShape = BTreeDelegateShapeDto.ThreeParamReusableStateful,
            WorkingStateTypeId = WorkingStateTypeId,
        },
    };

    /// <summary>Builds a Root → Sequence → (one Action per binding) asset with the given blackboard variables.</summary>
    private static BehaviorTreeAssetDto BuildAsset(
        Guid assetId, string name, IReadOnlyList<BlackboardVariableDto> variables,
        IReadOnlyList<(Guid VisualId, string Label, string StateField)> actionBindings)
    {
        var rootId = Guid.NewGuid();
        var seqId  = Guid.NewGuid();

        var root = new BTreeRootNodeDto { VisualId = rootId, DisplayLabel = "Root" };
        root.ChildVisualIds.Add(seqId);

        var seq = new BTreeSequenceNodeDto { VisualId = seqId, DisplayLabel = "Sequence" };

        var dto = new BehaviorTreeAssetDto
        {
            AssetId = assetId,
            Name = name,
            TargetNamespace = "Hrot.AI.Behaviors.Trees",
            BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
            ContextTypeName = "Fdp.Toolkit.Behavior.BTreeContext",
        };
        dto.Nodes.Add(root);
        dto.Nodes.Add(seq);

        foreach (var b in actionBindings)
        {
            seq.ChildVisualIds.Add(b.VisualId);
            dto.Nodes.Add(ActionNode(b.VisualId, b.Label, ParamVarName, b.StateField));
        }

        dto.Blackboard = new BlackboardBlockDto
        {
            Managed = true,
            TypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
        };
        foreach (var v in variables)
            dto.Blackboard.Variables.Add(v);

        return dto;
    }

    // ── Generator + Roslyn pipeline (copied from T20; do not modify T20) ───────────

    private static string[] GenerateBTreeSourcesWithBehaviorsRef(string json, string assetName)
    {
        var text   = new StringAdditionalText($"/p/{assetName}.btree.json", json);
        var driver = CSharpGeneratorDriver
            .Create(new BTreeJsonGenerator())
            .AddAdditionalTexts(new[] { (AdditionalText)text }.ToImmutableArrayCompat());

        var resolver = RoslynMRR.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies());
        var refs     = resolver.Resolve();

        var compilation = CSharpCompilation.Create(
            "Gen_" + assetName,
            Array.Empty<SyntaxTree>(),
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        result.Diagnostics.Should().BeEmpty(
            $"generator must produce no diagnostics for '{assetName}' " +
            "(a BTREE0002 means a struct-DTO type was not resolved or the validator rejected a binding)");

        result.GeneratedTrees.Length.Should().BeGreaterThanOrEqualTo(2,
            "must produce at least topology core + bridge (managed asset adds blackboard struct too)");

        return result.GeneratedTrees.Select(t => t.ToString()).ToArray();
    }

    private static (Assembly Assembly, AssemblyLoadContext Alc) CompileMultiAndLoad(
        string[] sources, string assemblyName)
    {
        var resolver = RoslynMRR.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies());
        var refs     = resolver.Resolve();

        var syntaxTrees = sources
            .Select((src, i) =>
            {
                var st = Microsoft.CodeAnalysis.Text.SourceText.From(
                    src, System.Text.Encoding.UTF8);
                return CSharpSyntaxTree.ParseText(
                    st, new CSharpParseOptions(LanguageVersion.Latest),
                    path: $"{assemblyName}_{i}.g.cs");
            })
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName, syntaxTrees, refs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                deterministic: true,
                allowUnsafe: true));

        using var pe  = new System.IO.MemoryStream();
        using var pdb = new System.IO.MemoryStream();
        var result = compilation.Emit(pe, pdb);

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id}({d.Location.GetMappedLineSpan().Path}:" +
                             $"{d.Location.GetMappedLineSpan().StartLinePosition.Line+1}): {d.GetMessage()}"));
            throw new InvalidOperationException(
                $"In-memory compilation of '{assemblyName}' failed:\n{errors}");
        }

        pe.Position  = 0;
        pdb.Position = 0;
        var alc = new AssemblyLoadContext($"S3Test_{assemblyName}", isCollectible: true);
        var asm = alc.LoadFromStream(pe, pdb);
        return (asm, alc);
    }

    private AiHotReloadCoordinator CreateScanCoordinator() =>
        new AiHotReloadCoordinator(new BehaviorRegistry(), _blueprintRegistry,
            new AiHotReloadCoordinatorOptions());

    /// <summary>
    /// Serialize the in-memory DTO → generate → compile → bridge-register into _liveRegistry,
    /// then return the registered BehaviorDefinition (with its StatefulWorkingSlots manifest)
    /// and the collectible ALC.
    /// </summary>
    private (BehaviorDefinition Def, AssemblyLoadContext Alc)
        BuildDefFromDto(BehaviorTreeAssetDto dto)
    {
        // Force required assemblies into the AppDomain so Roslyn's ForRuntimeAssemblies picks them up.
        GC.KeepAlive(typeof(Fbt.Compiler.FbtAutoDiscovery));     // forces Fbt.Compiler
        GC.KeepAlive(typeof(DemoCounterNodes.DemoCursorParams)); // forces Hrot.AI.Behaviors
        GC.KeepAlive(typeof(DemoCounterNodes.DemoCursorState));

        string assetName    = dto.Name;
        string registrarName = assetName + "Registrar";

        string json    = BTreeJsonServices.Serialize(dto);
        var srcs       = GenerateBTreeSourcesWithBehaviorsRef(json, assetName);
        var (asm, alc) = CompileMultiAndLoad(srcs, $"{assetName}ProofTest");

        using var coordinator = CreateScanCoordinator();
        var registrars = coordinator.ScanForRegistrars(asm);

        var bridge = registrars.FirstOrDefault(r => r.DeclaringType.Name == registrarName);
        bridge.Should().NotBeNull($"ScanForRegistrars must discover '{registrarName}'");

        var bpStaging = _blueprintRegistry.BeginStaging();
        var actionReg = new ActionRegistry<byte, BTreeContext>();
        var args = bridge!.Parameters
            .OrderBy(p => p.OrdinalIndex)
            .Select(p => p.ParameterType == typeof(BehaviorRegistry)
                         ? (object)_liveRegistry
                         : p.ParameterType == typeof(ActionRegistry<byte, BTreeContext>)
                           ? (object)actionReg
                           : (object)bpStaging)
            .ToArray();
        bridge.RegisterMethod.Invoke(null, args);

        _liveRegistry.TryGetId(assetName, out int id)
            .Should().BeTrue($"'{assetName}' must be registered in _liveRegistry after bridge invoke");
        _liveRegistry.TryGetDefinition(id, out var def)
            .Should().BeTrue("definition must be retrievable from _liveRegistry");
        def.Should().NotBeNull("definition must not be null");

        return (def!, alc);
    }

    // ⛔ CE-376: the slot-COUNT helper is deleted. It counted every occurrence slot including the
    //   two ROOT ones, so "expected 1, found 3" read as a product defect for two slices.
    //   RootParamsTestHarness.AssertAuthoredSlotsAre asserts WHICH keys are attached instead.

    private static unsafe bool TrySlotOffset(EntityRepository world, Fdp.Core.Entity entity, int slotKey)
    {
        // ⭐ B4: was THREE arms over the tier trio and knew nothing about the 256 tier.
        //   OccurrenceStoreAccess is the seam production uses for exactly this.
        byte* mem = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        return mem != null && BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out _);
    }

    private void AssignBehavior(EntityRepository world, Fdp.Core.Entity entity, string behaviorName)
    {
        var ingress = new BehaviorIngressSystem(_liveRegistry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = behaviorName,
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);
    }

    // ── TEST 1 ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Three Action nodes all bind the same Behavior-scoped variable "sharedCursor".
    /// Scope-aware key = FNV-1a(assetId, "sharedCursor") is identical for all three ⇒ the
    /// manifest dedups to ONE entry ⇒ ONE provisioned slot.
    /// </summary>
    [Fact]
    public void Assign_BehaviorScoped_ProvisionsOneSlot_ForSharedVar()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        BehaviorScoped_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BehaviorScoped_Body(out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var assetId = new Guid("b3000001-0000-0000-0000-000000000000");
        const string assetName = "S3SharedBehavior";
        const string sharedVar = "sharedCursor";

        var n1 = new Guid("b3100001-0000-0000-0000-000000000001");
        var n2 = new Guid("b3100001-0000-0000-0000-000000000002");
        var n3 = new Guid("b3100001-0000-0000-0000-000000000003");

        var dto = BuildAsset(
            assetId, assetName,
            new[] { ParamVar(ParamVarName), StateVar(sharedVar, WorkingStateScope.Behavior) },
            new[]
            {
                (n1, "Action_A", sharedVar),
                (n2, "Action_B", sharedVar),
                (n3, "Action_C", sharedVar),
            });

        var (def, alc) = BuildDefFromDto(dto);

        // ⭐⭐ CE-437 — three co-bound Behavior nodes still share ONE location, but that location is
        //   the behaviour's BLOCK (St.sharedCursor), not a side slot ⇒ the manifest declares none.
        //   ⛔ This used to assert one deduped manifest entry under the Behavior-scope key.
        int behaviorKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(
            assetId, WorkingStateScope.Behavior, Guid.Empty, sharedVar);
        (def.StatefulWorkingSlots ?? System.Array.Empty<StatefulSlotInfo>())
            .Should().BeEmpty("CE-437: the only stateful variable is block-resident");
        def.BlackboardLayoutType!.GetField("St")!.FieldType.GetField(sharedVar)
            .Should().NotBeNull("CE-437: the shared variable is a field of the block's State half");

        // Provisioning: one shared slot.
        var world = CreateWorld();
        Fdp.Core.Entity entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        RootStateAccess.EnsureRootState(world, entity);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

        AssignBehavior(world, entity, assetName);

        // ⭐ CE-376: assert WHICH slots are attached, not HOW MANY — a count also counts the two ROOT
        //   slots (root params, root cursor), which is why this read "1" and found 3.
        RootParamsTestHarness.AssertAuthoredSlotsAre(
            world, entity, System.Array.Empty<int>(),
            "CE-437: no authored side slot — the shared state is in the root block");
        TrySlotOffset(world, entity, behaviorKey).Should().BeFalse(
            "CE-437: nothing is attached under the old Behavior-scope key — one home");
        RootParamsTestHarness.ReadBlockState<DemoCounterNodes.DemoCursorState>(world, entity, def, sharedVar).Cursor
            .Should().Be(0, "the block is allocated wide enough to hold the shared state, and starts zeroed");

        world.Dispose();
        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ── TEST 2 ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mixed scopes: two Node-scoped variables (one binding each) + one Behavior-scoped variable
    /// (two bindings). Expect three distinct manifest entries (two per-node keys + one shared
    /// Behavior key; the two shared bindings dedup) ⇒ three provisioned slots.
    /// </summary>
    [Fact]
    public void Assign_MixedNodeAndBehaviorScope_SlotCountsCorrect()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        MixedScope_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void MixedScope_Body(out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var assetId = new Guid("b3000002-0000-0000-0000-000000000000");
        const string assetName = "S3MixedScope";

        var n1 = new Guid("b3200002-0000-0000-0000-000000000001");
        var n2 = new Guid("b3200002-0000-0000-0000-000000000002");
        var n3 = new Guid("b3200002-0000-0000-0000-000000000003");
        var n4 = new Guid("b3200002-0000-0000-0000-000000000004");

        var dto = BuildAsset(
            assetId, assetName,
            new[]
            {
                ParamVar(ParamVarName),
                StateVar("localA", WorkingStateScope.Node),
                StateVar("localB", WorkingStateScope.Node),
                StateVar("shared", WorkingStateScope.Behavior),
            },
            new[]
            {
                (n1, "Action_localA", "localA"),
                (n2, "Action_localB", "localB"),
                (n3, "Action_shared_1", "shared"),
                (n4, "Action_shared_2", "shared"),
            });

        var (def, alc) = BuildDefFromDto(dto);

        // ⭐ CE-437: two distinct Node keys stay side slots; the shared Behavior variable is in the block.
        //   ⛔ This used to be 3 entries (the shared one dedup'd to a third side slot).
        def.StatefulWorkingSlots.Should().NotBeNull("mixed stateful asset must carry a slot manifest");
        def.StatefulWorkingSlots!.Count.Should().Be(2,
            "two Node-scoped nodes (distinct keys) ⇒ 2 entries; the shared Behavior variable is block-resident");

        var world = CreateWorld();
        Fdp.Core.Entity entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        RootStateAccess.EnsureRootState(world, entity);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

        AssignBehavior(world, entity, assetName);

        // ⭐ CE-376: the manifest's OWN keys, so the two Node-scoped slots and the one shared
        //   Behavior slot are named rather than totalled. ⛔ The two ROOT slots are added by the
        //   helper — a count here silently absorbed them.
        RootParamsTestHarness.AssertAuthoredSlotsAre(
            world, entity, def.StatefulWorkingSlots!.Select(s => s.SlotKey),
            "two Node-scoped slots exactly as the manifest names them (CE-437: the shared one is in the block)");

        int behaviorKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(
            assetId, WorkingStateScope.Behavior, Guid.Empty, "shared");
        TrySlotOffset(world, entity, behaviorKey).Should().BeFalse(
            "CE-437: the shared variable has no side slot");
        RootParamsTestHarness.ReadBlockState<DemoCounterNodes.DemoCursorState>(world, entity, def, "shared").Cursor
            .Should().Be(0, "CE-437: the block holds the shared state");

        world.Dispose();
        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ── TEST 3 — CE-420 / CE-426 ──────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <c>CE-420</c>, closed by <c>CE-426</c> — <b>an authored default on a <c>Role=State</c>
    /// variable reaches the block.</b> 🔒 User, <c>2026-09-28</c>: <i>"working state zero init - isn't
    /// that wrong, no editor saved defaults here?"</i> ⛔ Before this the bake list held the packed
    /// (Input) fields only, so the default the editor offered and saved was dropped with no diagnostic.
    ///
    /// <para>⭐ And the second half is <c>CE-421</c>'s ruling: a RE-assign starts the block from empty
    /// again, so a mutated State returns to its AUTHORED DEFAULT — not to zero, and not to the value
    /// the previous assign left.</para>
    ///
    /// <para>⚠ Inverse-edit red-proof: delete the <c>EmitStateDefaultBake</c> call in
    /// <c>EmitParseParamsLocal</c> and both reads return 0.</para>
    /// </summary>
    [Fact]
    public void Assign_BakesAnAuthoredStateDefault_IntoTheBlock_AndReassignRestoresIt()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        StateDefault_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StateDefault_Body(out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var assetId = new Guid("b3000003-0000-0000-0000-000000000000");
        const string assetName = "S3StateDefault";
        var n1 = new Guid("b3300003-0000-0000-0000-000000000001");

        var stateVar = StateVar("shared", WorkingStateScope.Behavior);
        stateVar.DefaultValueJson = "{\"Cursor\":7}";

        var dto = BuildAsset(
            assetId, assetName,
            new[] { ParamVar(ParamVarName), stateVar },
            new[] { (n1, "Action_shared", "shared") });

        var (def, alc) = BuildDefFromDto(dto);

        var world = CreateWorld();
        Fdp.Core.Entity entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        RootStateAccess.EnsureRootState(world, entity);

        AssignBehavior(world, entity, assetName);
        RootParamsTestHarness.ReadBlockState<DemoCounterNodes.DemoCursorState>(world, entity, def, "shared").Cursor
            .Should().Be(7, "CE-420: the authored State default is BAKED into the block at assign");

        RootParamsTestHarness.ReadBlockState<DemoCounterNodes.DemoCursorState>(world, entity, def, "shared").Cursor = 99;
        AssignBehavior(world, entity, assetName);
        RootParamsTestHarness.ReadBlockState<DemoCounterNodes.DemoCursorState>(world, entity, def, "shared").Cursor
            .Should().Be(7, "CE-421: a re-assign starts from empty and re-bakes — the authored default, not 99, not 0");

        world.Dispose();
        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ── ALC GC helper (copied from T20) ───────────────────────────────────────────

    private static void AwaitAlcCollection(WeakReference<AssemblyLoadContext>[] refs)
    {
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (refs.All(w => !w.TryGetTarget(out _))) return;
            System.Threading.Thread.Sleep(50);
        }
    }
}
