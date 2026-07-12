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

using RoslynMRR = Hrot.Blueprints.Core.Compiler.Roslyn.MetadataReferenceResolver;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// S3-3 proof tests (Slice 3 — §4.4 Behavior-scope shared working state).
///
/// The production change under test bakes the stateful slot key <b>scope-aware</b> in BOTH
/// emit sites that must stay in lockstep:
///   • <c>BTreeBridgeEmitCore.EmitStatefulActionThunks</c> — the thunk's <c>const int __slotKey</c>
///     and its registry key <c>{MethodFqn}@{offset}@{slotKey}</c>;
///   • <c>BTreeEmitCore.EmitAction</c> — the topology blob key that each node dispatches through.
/// Both go through <c>BTreeBridgeEmitCore.ResolveStatefulSlotKey</c> (single source of truth).
///
/// For a Behavior-scoped variable, S3-2's key <c>FNV-1a(assetId, variableId)</c> is identical for
/// every co-bound node, so they dispatch to ONE thunk over the ONE shared slot provisioned by S3-4.
/// Node-scoped assets keep the legacy per-node key (byte-identical — Slice-2 untouched).
/// </summary>
public sealed class S3_BehaviorScopedThunkTests : IDisposable
{
    private const string MethodFqn = "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_AdvanceCursor";
    private const string ParamsTypeId = "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorParams";
    private const string WorkingStateTypeId = "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorState";

    private readonly BehaviorRegistry  _liveRegistry      = new();
    private readonly BlueprintRegistry _blueprintRegistry = new();

    public void Dispose() => _liveRegistry.Clear();

    // ── World factory ─────────────────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BehaviorState>();
        world.RegisterComponent<BrainBlackboard>();
        world.RegisterComponent<BrainBTreeState>();
        world.RegisterComponent<BlueprintBlackboard1024>();
        world.RegisterComponent<BlueprintBlackboard4096>();
        world.RegisterComponent<BlueprintBlackboard16384>();
        return world;
    }

    // ── DTO builders ──────────────────────────────────────────────────────────────

    private static BlackboardVariableDto StateVar(string name, WorkingStateScope scope, int limit) => new()
    {
        Name = name,
        Type = new BlackboardTypeRefDto { TypeId = ParamsTypeId },
        DefaultValueJson = $"{{\"Limit\":{limit}}}",
        Role = BlackboardVariableRole.State,
        Scope = scope,
    };

    private static BTreeActionNodeDto ActionNode(Guid visualId, string label, string targetField) => new()
    {
        VisualId = visualId,
        DisplayLabel = label,
        Action = new BTreeActionPayloadDto
        {
            MethodFqn = MethodFqn,
            ExpressionTargetField = targetField,
            DelegateShape = BTreeDelegateShapeDto.ThreeParamReusableStateful,
            WorkingStateTypeId = WorkingStateTypeId,
        },
    };

    private static BehaviorTreeAssetDto BuildAsset(
        Guid assetId, string name, IReadOnlyList<BlackboardVariableDto> variables,
        IReadOnlyList<(Guid VisualId, string Label, string TargetField)> actionBindings)
    {
        var root = new BTreeRootNodeDto { VisualId = Guid.NewGuid(), DisplayLabel = "Root" };
        var seq  = new BTreeSequenceNodeDto { VisualId = Guid.NewGuid(), DisplayLabel = "Sequence" };
        root.ChildVisualIds.Add(seq.VisualId);

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
            dto.Nodes.Add(ActionNode(b.VisualId, b.Label, b.TargetField));
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

    // ── Generator + Roslyn pipeline (mirrors T20 / S3_SharedSlotProvisioningTests) ──

    private static string[] GenerateSources(string json, string assetName)
    {
        var text   = new StringAdditionalText($"/p/{assetName}.btree.json", json);
        var driver = CSharpGeneratorDriver
            .Create(new BTreeJsonGenerator())
            .AddAdditionalTexts(new[] { (AdditionalText)text }.ToImmutableArrayCompat());

        var resolver = RoslynMRR.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies());
        var refs     = resolver.Resolve();
        var compilation = CSharpCompilation.Create(
            "Gen_" + assetName, Array.Empty<SyntaxTree>(), refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        result.Diagnostics.Should().BeEmpty(
            $"generator must produce no diagnostics for '{assetName}' (BTREE0002 = validator/DTO issue)");
        result.GeneratedTrees.Length.Should().BeGreaterThanOrEqualTo(2,
            "must produce at least topology core + bridge");
        return result.GeneratedTrees.Select(t => t.ToString()).ToArray();
    }

    private static (Assembly Assembly, AssemblyLoadContext Alc) CompileAndLoad(
        string[] sources, string assemblyName)
    {
        // Force STJ into the AppDomain before enumerating refs (emitted ParseParams uses it).
        GC.KeepAlive(typeof(System.Text.Json.JsonSerializer));
        var resolver = RoslynMRR.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies());
        var refs     = resolver.Resolve();

        var syntaxTrees = sources.Select((src, i) => CSharpSyntaxTree.ParseText(
            Microsoft.CodeAnalysis.Text.SourceText.From(src, System.Text.Encoding.UTF8),
            new CSharpParseOptions(LanguageVersion.Latest), path: $"{assemblyName}_{i}.g.cs")).ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName, syntaxTrees, refs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug, deterministic: true, allowUnsafe: true));

        using var pe  = new System.IO.MemoryStream();
        using var pdb = new System.IO.MemoryStream();
        var result = compilation.Emit(pe, pdb);
        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id}({d.Location.GetMappedLineSpan().Path}:" +
                             $"{d.Location.GetMappedLineSpan().StartLinePosition.Line + 1}): {d.GetMessage()}"));
            throw new InvalidOperationException($"In-memory compilation of '{assemblyName}' failed:\n{errors}");
        }
        pe.Position = 0; pdb.Position = 0;
        var alc = new AssemblyLoadContext($"S3ThunkTest_{assemblyName}", isCollectible: true);
        return (alc.LoadFromStream(pe, pdb), alc);
    }

    private (BehaviorDefinition Def, AssemblyLoadContext Alc) BuildDefFromDto(BehaviorTreeAssetDto dto)
    {
        GC.KeepAlive(typeof(Fbt.Compiler.FbtAutoDiscovery));
        GC.KeepAlive(typeof(DemoCounterNodes.DemoCursorParams));
        GC.KeepAlive(typeof(DemoCounterNodes.DemoCursorState));

        string assetName = dto.Name;
        string registrarName = assetName + "Registrar";
        string json = BTreeJsonServices.Serialize(dto);
        var srcs = GenerateSources(json, assetName);
        var (asm, alc) = CompileAndLoad(srcs, $"{assetName}ProofTest");

        using var coordinator = new AiHotReloadCoordinator(
            new BehaviorRegistry(), _blueprintRegistry, new AiHotReloadCoordinatorOptions());
        var registrars = coordinator.ScanForRegistrars(asm);
        var bridge = registrars.FirstOrDefault(r => r.DeclaringType.Name == registrarName);
        bridge.Should().NotBeNull($"ScanForRegistrars must discover '{registrarName}'");

        var bpStaging = _blueprintRegistry.BeginStaging();
        var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
        var args = bridge!.Parameters.OrderBy(p => p.OrdinalIndex)
            .Select(p => p.ParameterType == typeof(BehaviorRegistry) ? (object)_liveRegistry
                       : p.ParameterType == typeof(ActionRegistry<BrainBlackboard, BTreeContext>) ? (object)actionReg
                       : (object)bpStaging)
            .ToArray();
        bridge.RegisterMethod.Invoke(null, args);

        _liveRegistry.TryGetId(assetName, out int id).Should().BeTrue(
            $"'{assetName}' must be registered after bridge invoke");
        _liveRegistry.TryGetDefinition(id, out var def).Should().BeTrue("definition must be retrievable");
        def!.BTreeInterpreter.Should().NotBeNull("BTree interpreter must be non-null");
        return (def!, alc);
    }

    private void AssignBehavior(EntityRepository world, Fdp.Core.Entity entity, string behaviorName)
    {
        var ingress = new BehaviorIngressSystem(_liveRegistry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity = entity, BehaviorName = behaviorName, JsonParams = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);
    }

    private static unsafe int SlotCount(EntityRepository world, Fdp.Core.Entity entity)
    {
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        { ref var t = ref world.GetComponentRW<BlueprintBlackboard16384>(entity); fixed (byte* m = t.Memory) return BlueprintBlackboardPartitions.GetSlotCount(m); }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        { ref var t = ref world.GetComponentRW<BlueprintBlackboard4096>(entity); fixed (byte* m = t.Memory) return BlueprintBlackboardPartitions.GetSlotCount(m); }
        ref var t1 = ref world.GetComponentRW<BlueprintBlackboard1024>(entity); fixed (byte* m = t1.Memory) return BlueprintBlackboardPartitions.GetSlotCount(m);
    }

    private static unsafe int ReadCursor(EntityRepository world, Fdp.Core.Entity entity, int slotKey)
    {
        int Read(byte* mem)
        {
            BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off)
                .Should().BeTrue("shared slot must exist when reading cursor");
            return Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + off).Cursor;
        }
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        { ref var t = ref world.GetComponentRW<BlueprintBlackboard16384>(entity); fixed (byte* m = t.Memory) return Read(m); }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        { ref var t = ref world.GetComponentRW<BlueprintBlackboard4096>(entity); fixed (byte* m = t.Memory) return Read(m); }
        ref var t1 = ref world.GetComponentRW<BlueprintBlackboard1024>(entity); fixed (byte* m = t1.Memory) return Read(m);
    }

    // ── TEST 1: BehaviorScoped_TwoNodes_ShareOneSlot ──────────────────────────────

    /// <summary>
    /// Two Action nodes bind ONE Behavior-scoped variable "shared" (Limit=1). They dispatch to one
    /// thunk over one shared DemoCursorState slot. In a single tick the Sequence runs node A then
    /// node B on the SAME cursor: 0→1 (A: Success at Limit=1) then 1→2 (B: Success) ⇒ final Cursor=2.
    /// Independent per-node slots would instead leave two slots at Cursor=1 each. Asserting exactly
    /// ONE slot with Cursor=2 proves the writer node's change is visible to the reader node.
    /// </summary>
    [Fact]
    public void BehaviorScoped_TwoNodes_ShareOneSlot()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        ShareOneSlot_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe void ShareOneSlot_Body(out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var assetId = new Guid("b3300001-0000-0000-0000-000000000000");
        const string assetName = "S3ShareOneSlot";
        const string shared = "shared";
        var n1 = new Guid("b3310001-0000-0000-0000-000000000001");
        var n2 = new Guid("b3310001-0000-0000-0000-000000000002");

        var dto = BuildAsset(
            assetId, assetName,
            new[] { StateVar(shared, WorkingStateScope.Behavior, limit: 1) },
            new[] { (n1, "A", shared), (n2, "B", shared) });

        var (def, alc) = BuildDefFromDto(dto);

        int behaviorKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(
            assetId, WorkingStateScope.Behavior, Guid.Empty, shared);

        def.StatefulWorkingSlots.Should().NotBeNull();
        def.StatefulWorkingSlots!.Count.Should().Be(1, "two co-bound Behavior nodes share one slot");
        def.StatefulWorkingSlots[0].SlotKey.Should().Be(behaviorKey);

        var world = CreateWorld();
        Fdp.Core.Entity entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        AssignBehavior(world, entity, assetName);
        SlotCount(world, entity).Should().Be(1, "exactly one shared partition slot must be provisioned");

        // One tick: A then B advance the SAME cursor (0→1→2).
        var ctx = new BTreeContext { Self = entity, World = world };
        {
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
            var state = new BehaviorTreeState();
            def.BTreeInterpreter!.Tick(ref bb, ref state, ref ctx);
        }

        ReadCursor(world, entity, behaviorKey).Should().Be(2,
            "node A (0→1) then node B (1→2) mutate the SAME shared slot in one tick; " +
            "independent per-node slots would give Cursor=1 each");

        world.Dispose();
        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ── TEST 2: NodeScoped_StillBakedConst ────────────────────────────────────────

    /// <summary>
    /// Regression: a Node-scoped 2-node asset must bake the legacy per-node keys (no drift), so
    /// each node keeps an independent slot (the Slice-2 behavior verified by T20's
    /// TwoStatefulInstances_MaintainIndependentState). Asserts the emitted bridge contains a
    /// baked <c>const int __slotKey = {legacyKey}</c> for each node's legacy FNV-1a key and that
    /// the two keys differ.
    /// </summary>
    [Fact]
    public void NodeScoped_StillBakedConst()
    {
        var assetId = new Guid("b3300002-0000-0000-0000-000000000000");
        var n1 = new Guid("b3320002-0000-0000-0000-000000000001");
        var n2 = new Guid("b3320002-0000-0000-0000-000000000002");

        var dto = BuildAsset(
            assetId, "S3NodeScoped",
            new[]
            {
                StateVar("localA", WorkingStateScope.Node, limit: 3),
                StateVar("localB", WorkingStateScope.Node, limit: 5),
            },
            new[] { (n1, "A", "localA"), (n2, "B", "localB") });

        Func<string, int?> sizeResolver = t => t == ParamsTypeId ? 4 : (int?)null;
        string bridgeSrc = BTreeBridgeEmitCore.EmitBridge(dto, sizeResolver);

        int legacyKey1 = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, n1);
        int legacyKey2 = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, n2);

        legacyKey1.Should().NotBe(legacyKey2, "distinct nodes ⇒ distinct Node-scope slot keys (independent slots)");
        bridgeSrc.Should().Contain($"const int __slotKey = {legacyKey1};",
            "Node-scoped node A must bake the legacy per-node key (no drift from Slice-2)");
        bridgeSrc.Should().Contain($"const int __slotKey = {legacyKey2};",
            "Node-scoped node B must bake the legacy per-node key (no drift from Slice-2)");
    }

    // ── ALC GC helper ─────────────────────────────────────────────────────────────

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
