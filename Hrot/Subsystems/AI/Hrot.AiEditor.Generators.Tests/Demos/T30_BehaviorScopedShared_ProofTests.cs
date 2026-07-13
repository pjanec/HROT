using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
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
/// S3-G DEMO GATE (T30): proves the real PlatoonHillAttack commander runs its mutable working state
/// (<see cref="HillAttackMutableState"/>) on a single <b>Behavior</b>-scoped
/// <c>BlueprintBlackboard*</c> partition slot — the same slot aliased by every stateful commander node —
/// instead of the former <c>Blackboard1024</c> + <c>Unsafe.As</c> hack.
///
/// <para>The asset here is a faithful two-node slice of the real commander (the same node methods, param
/// DTO, and working-state type) so the proof runs through the full generate→compile→provision→tick path
/// without the EQS/roster infrastructure the wave-dispatch nodes need. <c>Action_CalculateSegments</c> and
/// <c>Condition_IsWaveCompleted</c> touch no <c>ctx.World</c> components on the paths exercised here, so a
/// bare commander is enough. The exhaustive runtime behaviour (DispatchWave → IsWaveCompleted over the
/// shared slot with live subordinates + EQS) is covered by <c>HillAttackIntegrationTests</c>.</para>
/// </summary>
public sealed class T30_BehaviorScopedShared_ProofTests : IDisposable
{
    private const string CalcSegments   = "Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.Action_CalculateSegments";
    private const string IsWaveComplete = "Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.Condition_IsWaveCompleted";
    private const string ParamsTypeId   = "Hrot.AI.Behaviors.Brains.PlatoonHillAttackParams";
    private const string StateTypeId    = "Hrot.AI.Behaviors.Brains.HillAttackMutableState";
    private const string ParamVarName   = "cfg";
    private const string StateVarName   = "state";

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

    // ── DTO builder: two stateful commander nodes sharing one Behavior-scoped State variable ──

    private static BehaviorTreeAssetDto BuildAsset(Guid assetId, string name, Guid n1, Guid n2)
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

        seq.ChildVisualIds.Add(n1);
        seq.ChildVisualIds.Add(n2);
        dto.Nodes.Add(StatefulNode(n1, "CalculateSegments", CalcSegments));
        dto.Nodes.Add(StatefulNode(n2, "IsWaveCompleted",  IsWaveComplete));

        dto.Blackboard = new BlackboardBlockDto
        {
            Managed = true,
            TypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
        };
        // Input param variable (packed inline). 90 m firing line / 30 m spacing ⇒ TotalSlots == 3.
        dto.Blackboard.Variables.Add(new BlackboardVariableDto
        {
            Name = ParamVarName,
            Type = new BlackboardTypeRefDto { TypeId = ParamsTypeId },
            DefaultValueJson = "{\"StartX\":0,\"StartY\":0,\"EndX\":90,\"EndY\":0,\"TankSpacing\":30}",
            Role = BlackboardVariableRole.Input,
        });
        // Behavior-scoped working-state variable (partition tier; excluded from inline packing).
        dto.Blackboard.Variables.Add(new BlackboardVariableDto
        {
            Name = StateVarName,
            Type = new BlackboardTypeRefDto { TypeId = StateTypeId },
            Role = BlackboardVariableRole.State,
            Scope = WorkingStateScope.Behavior,
        });
        return dto;
    }

    private static BTreeActionNodeDto StatefulNode(Guid visualId, string label, string methodFqn) => new()
    {
        VisualId = visualId,
        DisplayLabel = label,
        Action = new BTreeActionPayloadDto
        {
            MethodFqn = methodFqn,
            ExpressionTargetField = ParamVarName,
            WorkingStateTargetField = StateVarName,
            DelegateShape = BTreeDelegateShapeDto.ThreeParamReusableStateful,
            WorkingStateTypeId = StateTypeId,
        },
    };

    // ── Generator + Roslyn pipeline (mirrors S3_BehaviorScopedThunkTests) ──

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

    private static (Assembly Assembly, AssemblyLoadContext Alc) CompileAndLoad(string[] sources, string assemblyName)
    {
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
                .Select(d => $"{d.Id}: {d.GetMessage()}"));
            throw new InvalidOperationException($"In-memory compilation of '{assemblyName}' failed:\n{errors}");
        }
        pe.Position = 0; pdb.Position = 0;
        var alc = new AssemblyLoadContext($"T30Test_{assemblyName}", isCollectible: true);
        return (alc.LoadFromStream(pe, pdb), alc);
    }

    private (BehaviorDefinition Def, AssemblyLoadContext Alc) BuildDefFromDto(BehaviorTreeAssetDto dto)
    {
        GC.KeepAlive(typeof(Fbt.Compiler.FbtAutoDiscovery));
        GC.KeepAlive(typeof(HillAttackMutableState));
        GC.KeepAlive(typeof(PlatoonHillAttackParams));

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

        _liveRegistry.TryGetId(assetName, out int id).Should().BeTrue($"'{assetName}' must be registered");
        _liveRegistry.TryGetDefinition(id, out var def).Should().BeTrue("definition must be retrievable");
        def!.BTreeInterpreter.Should().NotBeNull();
        return (def!, alc);
    }

    private void AssignBehavior(EntityRepository world, Entity entity, string behaviorName)
    {
        var ingress = new BehaviorIngressSystem(_liveRegistry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity = entity, BehaviorName = behaviorName, JsonParams = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);
    }

    private static unsafe int SlotCount(EntityRepository world, Entity entity)
    {
        ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* m = t.Memory) return BlueprintBlackboardPartitions.GetSlotCount(m);
    }

    private static unsafe T ReadState<T>(EntityRepository world, Entity entity, int slotKey,
        Func<HillAttackMutableState, T> project)
    {
        ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* m = t.Memory)
        {
            BlueprintBlackboardPartitions.TryGetSlotOffset(m, slotKey, out int off)
                .Should().BeTrue("shared slot must exist");
            return project(Unsafe.AsRef<HillAttackMutableState>(m + off));
        }
    }

    private static unsafe void MutateState(EntityRepository world, Entity entity, int slotKey,
        RefAction mutate)
    {
        ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* m = t.Memory)
        {
            BlueprintBlackboardPartitions.TryGetSlotOffset(m, slotKey, out int off)
                .Should().BeTrue("shared slot must exist");
            mutate(ref Unsafe.AsRef<HillAttackMutableState>(m + off));
        }
    }

    private delegate void RefAction(ref HillAttackMutableState s);

    // ── TEST 1: shared state persists across nodes over one Behavior slot ──────────

    [Fact]
    public void HillAttack_SharedState_PersistsAcrossNodes()
    {
        var assetId = new Guid("77300001-0000-0000-0000-000000000000");
        const string assetName = "T30HillAttackShared";
        var n1 = new Guid("77310001-0000-0000-0000-000000000001");
        var n2 = new Guid("77310001-0000-0000-0000-000000000002");

        var dto = BuildAsset(assetId, assetName, n1, n2);
        var (def, alc) = BuildDefFromDto(dto);

        // Both stateful nodes bind the same Behavior-scoped variable ⇒ one shared slot.
        int slotKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(
            assetId, WorkingStateScope.Behavior, Guid.Empty, StateVarName);

        def.StatefulWorkingSlots.Should().NotBeNull();
        def.StatefulWorkingSlots!.Count.Should().Be(1, "two co-bound Behavior nodes share one slot");
        def.StatefulWorkingSlots[0].SlotKey.Should().Be(slotKey);
        def.StatefulWorkingSlots[0].WorkingStateType.Should().Be(typeof(HillAttackMutableState));
        def.HeavyDtoType.Should().BeNull("Blackboard1024 HeavyDtoType hack is gone");

        var world = CreateWorld();
        Entity commander = world.CreateEntity();
        world.AddComponent(commander, new BehaviorState());
        world.AddComponent(commander, new BrainBlackboard());
        world.AddComponent(commander, new BrainBTreeState());

        AssignBehavior(world, commander, assetName);
        SlotCount(world, commander).Should().Be(1, "exactly one shared partition slot is provisioned");

        // Pre-seed a sentinel so we can prove Action_CalculateSegments writes THIS slot, not a fresh one.
        MutateState(world, commander, slotKey, (ref HillAttackMutableState s) => s.TotalSlots = 99);

        var ctx = new BTreeContext { Self = commander, World = world };
        {
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(commander);
            var state = new BehaviorTreeState();
            def.BTreeInterpreter!.Tick(ref bb, ref state, ref ctx);
        }

        // Action_CalculateSegments (node 1) computed TotalSlots=3 into the shared slot (overwrote 99),
        // and Condition_IsWaveCompleted (node 2) then read ActiveAttackerCount==0 from the SAME slot.
        ReadState(world, commander, slotKey, s => s.TotalSlots).Should().Be(3,
            "CalculateSegments wrote TotalSlots into the shared slot (90 m / 30 m = 3), replacing the sentinel");
        ReadState(world, commander, slotKey, s => (int)s.ActiveAttackerCount).Should().Be(0,
            "CalculateSegments zeroed the tracker in the same slot IsWaveCompleted reads");

        // Storage-level cross-node proof: distinct nodes re-project the SAME slot, so a bitmask written
        // through one projection (as DispatchWave would) is visible through another (as IsWaveCompleted would).
        MutateState(world, commander, slotKey, (ref HillAttackMutableState s) => s.WaveUsedSlotsMask = 0b1011);
        ReadState(world, commander, slotKey, s => (int)s.WaveUsedSlotsMask).Should().Be(0b1011,
            "a bitmask written to the shared slot by one node is read back by another over the same slot");

        world.Dispose();
        alc.Unload();
    }

    // ── TEST 2: generated code no longer touches Blackboard1024 for this state ─────

    [Fact]
    public void HillAttack_NoBlackboard1024Access()
    {
        // Force the Hrot.AI.Behaviors assembly into the AppDomain so the generator's method-resolution
        // (over AppDomain runtime assemblies) sees the bound node methods.
        GC.KeepAlive(typeof(HillAttackCommanderNodes));
        GC.KeepAlive(typeof(HillAttackMutableState));
        GC.KeepAlive(typeof(PlatoonHillAttackParams));

        var assetId = new Guid("77300002-0000-0000-0000-000000000000");
        const string assetName = "T30HillAttackNoBb1024";
        var n1 = new Guid("77320002-0000-0000-0000-000000000001");
        var n2 = new Guid("77320002-0000-0000-0000-000000000002");

        var dto  = BuildAsset(assetId, assetName, n1, n2);
        var json = BTreeJsonServices.Serialize(dto);
        var srcs = GenerateSources(json, assetName);
        string all = string.Join("\n", srcs);

        // The stateful thunks must project the partition tier (BlueprintBlackboard*), never the
        // Blackboard1024 heavy component or an Unsafe.As<Blackboard1024, …> reinterpret.
        all.Should().Contain("BlueprintBlackboard", "stateful thunks project the partition tier");
        all.Should().Contain(StateTypeId.Replace('+', '.'),
            "working state is projected as HillAttackMutableState from the slot");

        // No bare Blackboard1024 (allowing the BlueprintBlackboard* tier names) and no Unsafe.As cast to it.
        Regex.IsMatch(all, @"(?<!Blueprint)(?<!\w)Blackboard1024").Should().BeFalse(
            "generated code must not reference the legacy Blackboard1024 heavy component for this state");
        all.Should().NotContain("GetComponentRW<global::Fdp.Toolkit.Behavior.Components.Blackboard1024>",
            "the GetComponentRW<Blackboard1024>() hack must be gone");
        all.Should().NotContain("Unsafe.As<byte, global::Hrot.AI.Behaviors.Brains.HillAttackMutableState>",
            "working state is projected from the slot pointer (Unsafe.AsRef), not reinterpreted over a component");
    }
}
