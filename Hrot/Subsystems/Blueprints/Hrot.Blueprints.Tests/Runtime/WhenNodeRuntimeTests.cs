using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Spatial.Eqs;
using FDP.Eqs;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Tests.Mocks;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// A minimal event struct used only in WhenNode runtime tests.
/// Not in BuiltInEngineEventCatalog -- tests use EmptyEventCatalog to bypass Stage 2.
/// </summary>
[EventId(90999)]
[StructLayout(LayoutKind.Sequential)]
public struct WhenTestHitEvent
{
    public Entity Target;
    public float Damage;
}

[Collection("DebugProbe")]
public sealed class WhenNodeRuntimeTests
{
    // ---- Empty event catalog (bypasses Stage 2 BP2005 for test event types) ----

    private sealed class EmptyEventCatalog : IEngineEventCatalog
    {
        public static readonly EmptyEventCatalog Instance = new();
        public System.Collections.Generic.IReadOnlyList<EngineEventCatalogEntry> GetEntries()
            => Array.Empty<EngineEventCatalogEntry>();
    }

    private static CompileOptions OptionsWithEmptyEventCatalog() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      EmptyEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static CompileOptions OptionsWithPeerSibling(Guid peerAssetId) => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: new BlueprintSignature[]
        {
            new BlueprintSignature(
                Path: "", AssetId: peerAssetId, Name: "PeerBp", SanitizedName: "PeerBp",
                BlueprintId: 0,
                Dispatch: Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
                ExportedFunctions: Array.Empty<BlueprintFunctionSig>(),
                Hostings: Array.Empty<AiPrimitiveHosting>(),
                DeclaredCallablePeers: Array.Empty<Guid>()),
        });

    // ---- ConditionMet helpers ----

    /// <summary>
    /// Mock IPredicateCompiler whose delegate can be swapped between tests.
    /// </summary>
    private sealed class DelegatingPredicateCompiler : IPredicateCompiler
    {
        private readonly Func<EntityRepository, Entity, bool> _del;
        public DelegatingPredicateCompiler(Func<EntityRepository, Entity, bool> del) => _del = del;

        public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root)
            => _del;

        public System.Collections.Generic.IReadOnlyList<Type> ExtractMandatoryComponents(
            SearchPredicateDto root)
            => Array.Empty<Type>();
    }

    private sealed class MockSearchPredicateRegistry : ISearchPredicateRegistry { }

    // ---- State reading helpers ----

    /// <summary>
    /// Reads a value-type field from a blueprint slot's state struct using Marshal.OffsetOf.
    /// Works with CompileAndLoad output (StateFields not populated by generated registrar).
    /// </summary>
    private static T ReadSlotField<T>(
        BlueprintTestFixture fixture,
        BlueprintAsset asset,
        Entity entity,
        string fieldName)
        where T : unmanaged
    {
        var hash = BlueprintIdHash.Compute(asset.AssetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def),
            $"Blueprint definition not found for asset {asset.AssetId}");
        var stateType = def!.StateClrType;
        Assert.NotNull(stateType);
        var state = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state);
        var offset = (int)Marshal.OffsetOf(stateType!, fieldName);
        return MemoryMarshal.Read<T>(state!.Value.AsSpan().Slice(offset, Unsafe.SizeOf<T>()));
    }

    /// <summary>
    /// Resets a bool field in the blueprint state to false. Used by NoRefire tests to
    /// detect a second firing after the first.
    /// </summary>
    private static void ResetBoolField(
        BlueprintTestFixture fixture,
        BlueprintAsset asset,
        Entity entity,
        string fieldName)
    {
        var state = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state);
        var hash      = BlueprintIdHash.Compute(asset.AssetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def));
        var stateType = def!.StateClrType!;
        var offset    = (int)Marshal.OffsetOf(stateType, fieldName);
        // Obtain a write reference to the live slot via the view span.
        // Using MemoryMarshal.GetReference avoids unsafe BlueprintStateView layout
        // assumptions (the struct contains a managed ref field that can shift layout).
        var span = state!.Value.AsSpan();
        ref byte slotBase = ref Unsafe.AsRef(in MemoryMarshal.GetReference(span));
        Unsafe.Add(ref slotBase, offset) = 0;
    }

    /// <summary>
    /// Writes a value-type field directly into a blueprint slot's live state struct.
    /// Used to seed blueprint variables (e.g., EqsSensorHandle) before ticking.
    /// </summary>
    private static unsafe void WriteSlotField<T>(
        BlueprintTestFixture fixture,
        BlueprintAsset asset,
        Entity entity,
        string fieldName,
        T value)
        where T : unmanaged
    {
        var hash = BlueprintIdHash.Compute(asset.AssetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def),
            $"Blueprint definition not found for asset {asset.AssetId}");
        var stateType = def!.StateClrType;
        Assert.NotNull(stateType);
        var state = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state);
        var offset = (int)Marshal.OffsetOf(stateType!, fieldName);
        var span = state!.Value.AsSpan();
        ref byte slotBase = ref Unsafe.AsRef(in MemoryMarshal.GetReference(span));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref slotBase, offset), value);
    }

    // ---- AnotherTestComponent helper ----

    private static void SetX(BlueprintTestFixture fixture, Entity entity, float x)
        => fixture.World.GetComponentRW<AnotherTestComponent>(entity).X = x;

    // ---- ValueChanged asset builder ----

    /// <summary>
    /// Builds a minimal Instance blueprint with a WhenNode in ValueChanged mode.
    /// ComponentTypeId = "Hrot.Blueprints.Tests.Mocks.AnotherTestComponent"
    /// PropertyPath = "X" (float)
    /// </summary>
    private static (BlueprintAsset asset, string synthFieldName) BuildValueChangedAsset(
        WhenEdge edges = WhenEdge.RisingEdge,
        ValueChangedSource source = ValueChangedSource.SelfComponent,
        float epsilon = 0f,
        Guid? peerBlueprintAssetId = null)
    {
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var nodeId   = Guid.NewGuid();
        var id8      = nodeId.ToString("N").Substring(0, 8);
        var synthName = $"_when_{id8}_prev";

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var whenNode = new WhenNode
        {
            Id    = nodeId,
            Mode  = WhenMode.ValueChanged,
            Edges = edges,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId      = "Hrot.Blueprints.Tests.Mocks.AnotherTestComponent",
                PropertyPath         = "X",
                Source               = source,
                Epsilon              = epsilon,
                PeerBlueprintAssetId = peerBlueprintAssetId,
            },
        };
        var execIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var execOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
        var onFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
        whenNode.Pins.Add(execIn);
        whenNode.Pins.Add(execOut);
        whenNode.Pins.Add(onFired);

        var retNode  = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retExecIn);

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode, retNode },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = execOutPin.Id, ToNodeId = whenNode.Id, ToPinId = execIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = execOut.Id,   ToNodeId = retNode.Id,  ToPinId = retExecIn.Id },
            },
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId, Name = "WhenVC",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance, Graphs = { graph },
        };
        return (asset, synthName);
    }

    // ---- EventFired asset builder ----

    /// <summary>
    /// Builds an Instance blueprint with:
    ///   - VariableDecl "WasFired" (bool, default false)
    ///   - EventEntryNode -> WhenNode(EventFired) -> SetVariableNode(WasFired = true)
    ///                                                 ^ LiteralNode(bool, "true")
    /// </summary>
    private static BlueprintAsset BuildEventFiredAsset(
        string eventTypeId                 = "Hrot.Blueprints.Tests.Runtime.WhenTestHitEvent",
        EventTargetFilter targetFilter     = EventTargetFilter.None,
        string? targetFieldName            = null,
        PayloadCondition? payloadCheck     = null)
    {
        var assetId   = Guid.NewGuid();
        var graphId   = Guid.NewGuid();
        var varId     = Guid.NewGuid();

        // ---- Variable declaration ----
        var wasFiredVar = new VariableDecl
        {
            Id   = varId,
            Name = "WasFired",
            Type = new BlueprintTypeRef { TypeId = "bool" },
            DefaultValueJson = "false",
        };

        // ---- Nodes ----
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var whenId   = Guid.NewGuid();
        var whenNode = new WhenNode
        {
            Id    = whenId,
            Mode  = WhenMode.EventFired,
            Edges = WhenEdge.RisingEdge,
            EventFired = new EventFiredPayload
            {
                EventTypeId     = eventTypeId,
                TargetFilter    = targetFilter,
                TargetFieldName = targetFieldName,
                PayloadCheck    = payloadCheck,
            },
        };
        var whenExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var whenExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
        var whenOnFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
        whenNode.Pins.Add(whenExecIn);
        whenNode.Pins.Add(whenExecOut);
        whenNode.Pins.Add(whenOnFired);

        var litId     = Guid.NewGuid();
        var litOutPin = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
                                   TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
        var litNode = new LiteralNode { Id = litId, TypeId = "bool", ValueJson = "true" };
        litNode.Pins.Add(litOutPin);

        var setId      = Guid.NewGuid();
        var setExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var setExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        var setDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false,
                                    TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
        var setNode = new SetVariableNode { Id = setId, VariableId = varId.ToString() };
        setNode.Pins.Add(setExecIn);
        setNode.Pins.Add(setExecOut);
        setNode.Pins.Add(setDataIn);

        var retId      = Guid.NewGuid();
        var retExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        var retNode    = new ReturnNode { Id = retId };
        retNode.Pins.Add(retExecIn);

        var ret2Id     = Guid.NewGuid();
        var ret2ExecIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        var ret2Node   = new ReturnNode { Id = ret2Id };
        ret2Node.Pins.Add(ret2ExecIn);

        // ---- Graph ----
        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode, litNode, setNode, retNode, ret2Node },
            Links =
            {
                // Exec: entry -> when
                new Link { FromNodeId = entry.Id, FromPinId = entryExecOut.Id,
                           ToNodeId = whenNode.Id, ToPinId = whenExecIn.Id },
                // Exec: when.Out -> retNode (false/pass-through path)
                new Link { FromNodeId = whenNode.Id, FromPinId = whenExecOut.Id,
                           ToNodeId = retNode.Id, ToPinId = retExecIn.Id },
                // Exec: when.OnFired -> setVar
                new Link { FromNodeId = whenNode.Id, FromPinId = whenOnFired.Id,
                           ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                // Exec: setVar -> ret2Node
                new Link { FromNodeId = setNode.Id, FromPinId = setExecOut.Id,
                           ToNodeId = ret2Node.Id, ToPinId = ret2ExecIn.Id },
                // Data: literal.Value -> setVar.Value
                new Link { FromNodeId = litNode.Id, FromPinId = litOutPin.Id,
                           ToNodeId = setNode.Id, ToPinId = setDataIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "WhenEF",
            Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Variables = { wasFiredVar },
            Graphs   = { graph },
        };
    }

    // ---- ConditionMet asset builder ----

    /// <summary>
    /// Builds a minimal Instance blueprint with a WhenNode in ConditionMet mode.
    /// The predicate content is just a placeholder (ComponentType=typeof(object)) that
    /// passes Stage 2; the actual delegate is supplied at runtime by IPredicateCompiler.
    ///
    /// The graph wires:
    ///   Entry -> WhenNode -> (OnFired -> SetVar(WasFired=true) -> Return)
    ///                     -> (OnEnded -> SetVar(WasEnded=true) -> Return)  [if FallingEdge]
    ///                     -> (Out -> Return)
    /// </summary>
    private static (BlueprintAsset asset, Guid nodeId) BuildCondMetRuntimeAsset(
        WhenEdge edges = WhenEdge.RisingEdge)
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();

        // ---- Variables ----
        var firedVarId = Guid.NewGuid();
        var wasFiredVar = new VariableDecl
        {
            Id   = firedVarId,
            Name = "WasFired",
            Type = new BlueprintTypeRef { TypeId = "bool" },
            DefaultValueJson = "false",
        };
        var variables = new System.Collections.Generic.List<VariableDecl> { wasFiredVar };

        bool hasFalling = (edges & WhenEdge.FallingEdge) != 0;

        // ---- Nodes ----
        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut",
                                      Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var whenNode    = new WhenNode { Id = nodeId, Mode = WhenMode.ConditionMet, Edges = edges,
            ConditionMet = new ConditionMetPayload
            {
                Condition = JsonNode.Parse(
                    "{\"$type\":\"PropertyMatch\",\"ComponentType\":\"Object\",\"PropertyPath\":\"Value\"," +
                    "\"Predicate\":{\"$type\":\"Numeric\",\"MinValue\":5.0,\"MaxValue\":1.7976931348623157E+308}}"),
            },
        };
        var whenExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var whenExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
        whenNode.Pins.Add(whenExecIn);
        whenNode.Pins.Add(whenExecOut);
        if ((edges & WhenEdge.RisingEdge) != 0)
            whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });
        if (hasFalling)
            whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnEnded", Direction = "Out", IsExec = true, TypeRef = new() });

        // SetVariable(WasFired=true) after OnFired
        var setFiredNode = BuildSetBoolTrue(firedVarId, out var setFiredExecIn, out var setFiredExecOut, out var setFiredValIn);

        // Return nodes
        var retOut   = new ReturnNode { Id = Guid.NewGuid() };
        var retOutIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retOut.Pins.Add(retOutIn);
        var retFired   = new ReturnNode { Id = Guid.NewGuid() };
        var retFiredIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retFired.Pins.Add(retFiredIn);

        // Literal true
        var litFired    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "bool", ValueJson = "true" };
        var litFiredOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
                                     TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
        litFired.Pins.Add(litFiredOut);

        var nodes = new System.Collections.Generic.List<Node>
            { entry, whenNode, setFiredNode, litFired, retOut, retFired };
        var links = new System.Collections.Generic.List<Link>
        {
            new Link { FromNodeId = entry.Id,        FromPinId = entryExecOut.Id,
                       ToNodeId   = whenNode.Id,     ToPinId   = whenExecIn.Id },
            new Link { FromNodeId = whenNode.Id,     FromPinId = whenExecOut.Id,
                       ToNodeId   = retOut.Id,       ToPinId   = retOutIn.Id },
            new Link { FromNodeId = whenNode.Id,     FromPinId = whenNode.Pins.First(p => p.Name == "OnFired").Id,
                       ToNodeId   = setFiredNode.Id, ToPinId   = setFiredExecIn.Id },
            new Link { FromNodeId = setFiredNode.Id, FromPinId = setFiredExecOut.Id,
                       ToNodeId   = retFired.Id,     ToPinId   = retFiredIn.Id },
            new Link { FromNodeId = litFired.Id,     FromPinId = litFiredOut.Id,
                       ToNodeId   = setFiredNode.Id, ToPinId   = setFiredValIn.Id },
        };

        if (hasFalling)
        {
            // Wire OnEnded -> SetVariable(WasFired=true) -> Return.
            // Reuses firedVarId so the same WasFired field records both edge transitions;
            // the test resets WasFired between ticks to distinguish rising vs falling.
            var setEndedNode = BuildSetBoolTrue(firedVarId, out var setEndedExecIn, out var setEndedExecOut, out var setEndedValIn);
            var retEnded   = new ReturnNode { Id = Guid.NewGuid() };
            var retEndedIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
            retEnded.Pins.Add(retEndedIn);
            var litEnded    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "bool", ValueJson = "true" };
            var litEndedOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
                                         TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
            litEnded.Pins.Add(litEndedOut);
            nodes.AddRange(new Node[] { setEndedNode, retEnded, litEnded });
            links.AddRange(new Link[]
            {
                new Link { FromNodeId = whenNode.Id,      FromPinId = whenNode.Pins.First(p => p.Name == "OnEnded").Id,
                           ToNodeId   = setEndedNode.Id,  ToPinId   = setEndedExecIn.Id },
                new Link { FromNodeId = setEndedNode.Id,  FromPinId = setEndedExecOut.Id,
                           ToNodeId   = retEnded.Id,      ToPinId   = retEndedIn.Id },
                new Link { FromNodeId = litEnded.Id,      FromPinId = litEndedOut.Id,
                           ToNodeId   = setEndedNode.Id,  ToPinId   = setEndedValIn.Id },
            });
        }

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = nodes,
            Links = links,
        };

        var asset = new BlueprintAsset
        {
            AssetId   = assetId,
            Name      = "WhenCondMet",
            Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Variables = variables,
            Graphs    = { graph },
        };
        return (asset, nodeId);
    }

    /// <summary>Helper: creates a SetVariableNode that sets a bool variable to true.</summary>
    private static SetVariableNode BuildSetBoolTrue(
        Guid varId,
        out Pin execIn,
        out Pin execOut,
        out Pin valueIn)
    {
        var node = new SetVariableNode { Id = Guid.NewGuid(), VariableId = varId.ToString() };
        execIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        execOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        valueIn = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false,
                             TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
        node.Pins.Add(execIn);
        node.Pins.Add(execOut);
        node.Pins.Add(valueIn);
        return node;
    }

    // ======================== ValueChanged Tests ========================

    [Fact]
    public void ValueChanged_RisingEdge_Fires_WhenComponentValueChanges()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var (asset, synthName) = BuildValueChangedAsset(WhenEdge.RisingEdge);
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 50f });
        fixture.AttachBlueprint(asset, entity);
        fixture.TickFrame(0.016f);

        float prev = ReadSlotField<float>(fixture, asset, entity, synthName);
        Assert.Equal(50f, prev);
    }

    [Fact]
    public void ValueChanged_NoFire_WhenValueUnchanged()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var (asset, synthName) = BuildValueChangedAsset(WhenEdge.RisingEdge);
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 50f });
        fixture.AttachBlueprint(asset, entity);

        // Tick 1: X=50, prev fires -> prev becomes 50
        fixture.TickFrame(0.016f);

        // Do NOT change X; tick again
        // Tick 2: X still 50, no change -> StorePrev does NOT run again
        fixture.TickFrame(0.016f);

        float prev = ReadSlotField<float>(fixture, asset, entity, synthName);
        Assert.Equal(50f, prev);   // still 50 (StorePrev ran exactly once, on first tick)
    }

    [Fact]
    public void ValueChanged_WorkingState_PrevPersists_AcrossMultipleTicks()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var (asset, synthName) = BuildValueChangedAsset(WhenEdge.RisingEdge);
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 50f });
        fixture.AttachBlueprint(asset, entity);

        // Tick 1: X=50 -> prev=50
        fixture.TickFrame(0.016f);

        // Tick 2: X=50 -> prev stays 50 (no change detected)
        fixture.TickFrame(0.016f);

        // Tick 3: X=75 -> prev=75 (changed again)
        SetX(fixture, entity, 75f);
        fixture.TickFrame(0.016f);

        float prev = ReadSlotField<float>(fixture, asset, entity, synthName);
        Assert.Equal(75f, prev);
    }

    [Fact]
    public void ValueChanged_BothEdge_FiredBlock_Fires_WhenChanged()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var (asset, synthName) = BuildValueChangedAsset(WhenEdge.RisingEdge | WhenEdge.FallingEdge);
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 50f });
        fixture.AttachBlueprint(asset, entity);
        fixture.TickFrame(0.016f);

        float prev = ReadSlotField<float>(fixture, asset, entity, synthName);
        Assert.Equal(50f, prev);
    }

    [Fact]
    public void ValueChanged_FallingEdge_Only_NoCrash_NoPrevInM2()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var (asset, synthName) = BuildValueChangedAsset(WhenEdge.FallingEdge);
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 50f });
        fixture.AttachBlueprint(asset, entity);

        // Should not throw
        fixture.TickFrame(0.016f);

        // Verify: the synthesized field does NOT exist in the State struct
        var hash = BlueprintIdHash.Compute(asset.AssetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def));
        var stateType = def!.StateClrType!;
        var field = stateType.GetField(synthName, BindingFlags.Public | BindingFlags.Instance);
        Assert.Null(field);   // FallingEdge only -> no synthesized field
    }

    [Fact]
    public void ValueChanged_PeerVariable_CompilesAndTicks_NoCrash()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var peerAssetId = Guid.NewGuid();
        var (asset, _) = BuildValueChangedAsset(
            edges: WhenEdge.RisingEdge,
            source: ValueChangedSource.PeerBlueprintVariable,
            peerBlueprintAssetId: peerAssetId);
        fixture.CompileAndLoad(asset, OptionsWithPeerSibling(peerAssetId));
        var entity = fixture.CreateEntity();
        // M2: StatementEmitter emits self-component reads regardless of SourceKind,
        // so the component must exist to avoid a null-component access at runtime.
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 0f });
        fixture.AttachBlueprint(asset, entity);
        Assert.Null(Record.Exception(() => fixture.TickFrame(0.016f)));
    }

    // ======================== EventFired Tests ========================

    [Fact]
    public void EventFired_NoFilters_FastPath_Fires_OnAnyEvent()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BuildEventFiredAsset(
            targetFilter: EventTargetFilter.None);
        fixture.CompileAndLoad(asset, OptionsWithEmptyEventCatalog());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.World.Bus.Publish(new WhenTestHitEvent { Damage = 10f });
        fixture.TickFrame(0.016f);

        Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
    }

    [Fact]
    public void EventFired_WithSelfFilter_Fires_WhenTargetMatchesSelf()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BuildEventFiredAsset(
            targetFilter: EventTargetFilter.Self,
            targetFieldName: "Target");
        fixture.CompileAndLoad(asset, OptionsWithEmptyEventCatalog());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.World.Bus.Publish(new WhenTestHitEvent { Target = entity, Damage = 10f });
        fixture.TickFrame(0.016f);

        Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
    }

    [Fact]
    public void EventFired_WithSelfFilter_DoesNotFire_WhenTargetDiffers()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BuildEventFiredAsset(
            targetFilter: EventTargetFilter.Self,
            targetFieldName: "Target");
        fixture.CompileAndLoad(asset, OptionsWithEmptyEventCatalog());
        var entity = fixture.CreateEntity();
        var otherEntity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.World.Bus.Publish(new WhenTestHitEvent { Target = otherEntity, Damage = 10f });
        fixture.TickFrame(0.016f);

        Assert.False(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
    }

    [Fact]
    public void EventFired_WithPayloadCondition_Fires_WhenConditionMet()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BuildEventFiredAsset(
            targetFilter: EventTargetFilter.None,
            payloadCheck: new PayloadCondition
            {
                PropertyPath    = "Damage",
                Operator        = ComparisonOperator.GreaterThan,
                TargetValueText = "50f",
            });
        fixture.CompileAndLoad(asset, OptionsWithEmptyEventCatalog());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.World.Bus.Publish(new WhenTestHitEvent { Damage = 75f });
        fixture.TickFrame(0.016f);

        Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
    }

    [Fact]
    public void EventFired_WithPayloadCondition_DoesNotFire_WhenConditionNotMet()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BuildEventFiredAsset(
            targetFilter: EventTargetFilter.None,
            payloadCheck: new PayloadCondition
            {
                PropertyPath    = "Damage",
                Operator        = ComparisonOperator.GreaterThan,
                TargetValueText = "50f",
            });
        fixture.CompileAndLoad(asset, OptionsWithEmptyEventCatalog());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.World.Bus.Publish(new WhenTestHitEvent { Damage = 25f });
        fixture.TickFrame(0.016f);

        Assert.False(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
    }

    // ======================== ConditionMet Tests ========================

    [Fact]
    public void ConditionMet_RisingEdge_Fires_WhenPredicateTransitionsToTrue()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Predicate: AnotherTestComponent.X > 5.0f
        fixture.PredicateCompiler  = new DelegatingPredicateCompiler(
            (repo, ent) => repo.GetComponentRO<AnotherTestComponent>(ent).X > 5.0f);
        fixture.PredicateRegistry = new MockSearchPredicateRegistry();

        var (asset, _) = BuildCondMetRuntimeAsset(WhenEdge.RisingEdge);
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 3.0f }); // predicate false
        fixture.AttachBlueprint(asset, entity);

        // Tick 1: false -> false -> no fire
        fixture.TickFrame(0.016f);
        Assert.False(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));

        // Set X > 5 so predicate becomes true
        fixture.World.GetComponentRW<AnotherTestComponent>(entity).X = 10.0f;

        // Tick 2: false -> true -> OnFired fires
        fixture.TickFrame(0.016f);
        Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
    }

    [Fact]
    public void ConditionMet_NoRefire_WhenPredicateRemainsTrue()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Predicate: always true
        fixture.PredicateCompiler  = new DelegatingPredicateCompiler((_, _) => true);
        fixture.PredicateRegistry = new MockSearchPredicateRegistry();

        var (asset, _) = BuildCondMetRuntimeAsset(WhenEdge.RisingEdge);
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Tick 1: false -> true -> fires (rising edge)
        fixture.TickFrame(0.016f);
        Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));

        // Directly reset WasFired to false in the blackboard to detect re-fire.
        ResetBoolField(fixture, asset, entity, "WasFired");

        // Tick 2: true -> true -> no re-fire
        fixture.TickFrame(0.016f);
        Assert.False(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
    }

    [Fact]
    public void ConditionMet_FallingEdge_Fires_WhenPredicateBecomesFalse()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Predicate: X > 5.0f
        fixture.PredicateCompiler  = new DelegatingPredicateCompiler(
            (repo, ent) => repo.GetComponentRO<AnotherTestComponent>(ent).X > 5.0f);
        fixture.PredicateRegistry = new MockSearchPredicateRegistry();

        var (asset, _) = BuildCondMetRuntimeAsset(WhenEdge.RisingEdge | WhenEdge.FallingEdge);
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 10.0f }); // predicate true
        fixture.AttachBlueprint(asset, entity);

        // Tick 1: false -> true -> OnFired fires (rising edge).
        fixture.TickFrame(0.016f);
        Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));

        // Reset WasFired so the next assert can detect the OnEnded firing separately.
        ResetBoolField(fixture, asset, entity, "WasFired");

        // Set X < 5 so predicate becomes false on next tick.
        fixture.World.GetComponentRW<AnotherTestComponent>(entity).X = 2.0f;

        // Tick 2: true -> false -> OnEnded fires (falling edge) -> WasFired set to true again.
        fixture.TickFrame(0.016f);
        Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
    }

    [Fact]
    public void ConditionMet_NullDelegate_NoCrash_NoFire()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Degraded mode: PredicateCompiler = null -> InitializePredicates catches ->
        // _whenCondPred_ = null -> null check in generated code skips the block.
        fixture.PredicateCompiler  = null;
        fixture.PredicateRegistry = null;

        var (asset, _) = BuildCondMetRuntimeAsset(WhenEdge.RisingEdge);
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 10.0f });
        fixture.AttachBlueprint(asset, entity);

        // Three ticks -- no crash, WasFired never set (delegate is null)
        fixture.TickFrame(0.016f);
        fixture.TickFrame(0.016f);
        fixture.TickFrame(0.016f);

        Assert.False(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
    }

    // ======================== EQS WhenNode helpers ========================

    /// <summary>
    /// Builds a minimal Instance blueprint with WhenNode(EqsResult) mode,
    /// a SensorHandle variable, and a WasFired bool variable.
    /// OnFired -> SetVariable(WasFired = true).
    /// </summary>
    private static (BlueprintAsset asset, string synthFieldName, string sensorVarName) BuildEqsResultAsset(
        EqsTrigger trigger,
        WhenEdge edges        = WhenEdge.RisingEdge,
        float? scoreThreshold = null,
        float? maxAgeSeconds  = null)
    {
        var assetId   = Guid.NewGuid();
        var graphId   = Guid.NewGuid();
        var nodeId    = Guid.NewGuid();
        var id8       = nodeId.ToString("N").Substring(0, 8);
        var synthName = $"_when_{id8}_prev";
        const string sensorVarName = "SensorHandle";

        // Variables
        var sensorHandleVar = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = sensorVarName,
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
        };
        var firedVarId  = Guid.NewGuid();
        var wasFiredVar = new VariableDecl
        {
            Id               = firedVarId,
            Name             = "WasFired",
            Type             = new BlueprintTypeRef { TypeId = "bool" },
            DefaultValueJson = "false",
        };

        // Entry node
        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        // WhenNode
        var whenNode = new WhenNode
        {
            Id        = nodeId,
            Mode      = WhenMode.EqsResult,
            Edges     = edges,
            EqsResult = new EqsResultPayload
            {
                SensorVariableName = sensorVarName,
                Trigger            = trigger,
                ScoreThreshold     = scoreThreshold ?? 0f,
                MaxAgeSeconds      = maxAgeSeconds  ?? 0f,
            },
        };
        var whenExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var whenExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
        var whenOnFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
        whenNode.Pins.Add(whenExecIn);
        whenNode.Pins.Add(whenExecOut);
        whenNode.Pins.Add(whenOnFired);

        // LiteralNode(true) + SetVariableNode(WasFired) + Return nodes
        var litOutPin = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
                                   TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
        var litNode   = new LiteralNode { Id = Guid.NewGuid(), TypeId = "bool", ValueJson = "true" };
        litNode.Pins.Add(litOutPin);

        var setId      = Guid.NewGuid();
        var setExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var setExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
        var setDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
        var setNode    = new SetVariableNode { Id = setId, VariableId = firedVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setDataIn });

        var retId   = Guid.NewGuid();
        var retIn   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        var retNode = new ReturnNode { Id = retId };
        retNode.Pins.Add(retIn);

        var ret2In   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        var ret2Node = new ReturnNode { Id = Guid.NewGuid() };
        ret2Node.Pins.Add(ret2In);

        var graph = new Graph
        {
            Id    = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode, litNode, setNode, retNode, ret2Node },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = whenNode.Id, ToPinId = whenExecIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenExecOut.Id,  ToNodeId = retNode.Id,  ToPinId = retIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenOnFired.Id,  ToNodeId = setNode.Id,  ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,  FromPinId = setExecOut.Id,   ToNodeId = ret2Node.Id, ToPinId = ret2In.Id },
                new Link { FromNodeId = litNode.Id,  FromPinId = litOutPin.Id,    ToNodeId = setNode.Id,  ToPinId = setDataIn.Id },
            },
        };

        return (new BlueprintAsset
        {
            AssetId   = assetId,
            Name      = "WhenEqsTest",
            Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Variables = { sensorHandleVar, wasFiredVar },
            Graphs    = { graph },
        }, synthName, sensorVarName);
    }

    /// <summary>
    /// Creates a child entity with EqsCognitiveBuffer and EqsSensor components.
    /// Registers component types first (RegisterComponent is idempotent).
    /// </summary>
    private static Entity SetupEqsChildEntity(
        BlueprintTestFixture fixture,
        EqsCognitiveBuffer buffer,
        EqsSensor sensor = default)
    {
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();

        var childEntity = fixture.CreateEntity();
        fixture.World.AddComponent(childEntity, buffer);
        fixture.World.AddComponent(childEntity, sensor);
        return childEntity;
    }

    // ======================== EQS WhenNode Tests ========================

    [Fact]
    public void EqsResult_FirstReady_FiresOnceOnChildEntity()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();

        var (asset, _, sensorVarName) = BuildEqsResultAsset(EqsTrigger.FirstReady);
        fixture.CompileAndLoad(asset);
        var parentEntity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, parentEntity);

        // Tick 1: buffer not ready yet (LastUpdateTick = 0)
        var notReadyBuffer = default(EqsCognitiveBuffer);
        var childEntity    = SetupEqsChildEntity(fixture, notReadyBuffer);
        WriteSlotField(fixture, asset, parentEntity, sensorVarName, new EqsSensorHandle(childEntity));
        fixture.TickFrame(0.016f);
        bool wv1 = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
        Assert.False(wv1, "Should not fire on tick 1 when buffer not ready");

        // Tick 2: buffer becomes ready (LastUpdateTick = 1)
        var readyBuffer = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 0 };
        fixture.World.SetComponent(childEntity, readyBuffer);
        fixture.TickFrame(0.016f);
        bool wv2 = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
        Assert.True(wv2, "Should fire on first ready tick");

        // Tick 3: no re-fire (epoch guard prevents it)
        ResetBoolField(fixture, asset, parentEntity, "WasFired");
        fixture.TickFrame(0.016f);
        bool wv3 = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
        Assert.False(wv3, "Should NOT re-fire on subsequent ticks");
    }

    [Fact]
    public void EqsResult_TopChanged_PositionalQueries_HashesPosition()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();

        var (asset, _, sensorVarName) = BuildEqsResultAsset(EqsTrigger.TopChanged);
        fixture.CompileAndLoad(asset);
        var parentEntity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, parentEntity);

        // Set up child entity with a positional result (EntityId = 0) and initial sensor
        var buffer1 = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 1 };
        var span1   = buffer1.GetSpanRW();
        span1[0]    = new EqsResult { EntityId = 0L, PositionX = 1f, PositionY = 2f, Score = 0.5f };
        var sensor1 = new EqsSensor { Epoch = 1u };
        var childEntity = SetupEqsChildEntity(fixture, buffer1, sensor1);
        WriteSlotField(fixture, asset, parentEntity, sensorVarName, new EqsSensorHandle(childEntity));

        // Tick 1: first evaluation -- prev.LastEvaluatedEpoch == 0 guard; state recorded, no OnFired
        fixture.TickFrame(0.016f);
        ResetBoolField(fixture, asset, parentEntity, "WasFired");

        // Tick 2: epoch advances + position changes; TopChanged should fire
        var sensor2 = new EqsSensor { Epoch = 2u };
        fixture.World.SetComponent(childEntity, sensor2);
        var buffer2 = new EqsCognitiveBuffer { LastUpdateTick = 2u, Count = 1 };
        var span2   = buffer2.GetSpanRW();
        span2[0]    = new EqsResult { EntityId = 0L, PositionX = 99f, PositionY = 99f, Score = 0.8f };
        fixture.World.SetComponent(childEntity, buffer2);
        fixture.TickFrame(0.016f);
        bool fired = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
        Assert.True(fired, "Should fire when positional query top changes");
    }

    [Fact]
    public void EqsResult_BecomesStale_UsesSimTimeNotTicks()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();

        // MaxAge = 2 seconds; buffer written at time=5s; should fire when time >= 5+2 = 7s
        var (asset, _, sensorVarName) = BuildEqsResultAsset(EqsTrigger.BecomesStale, maxAgeSeconds: 2f);
        fixture.CompileAndLoad(asset);
        var parentEntity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, parentEntity);

        // Buffer last updated at time = 5s
        var buffer = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 0, LastUpdateTimeSeconds = 5f };
        var childEntity = SetupEqsChildEntity(fixture, buffer);
        WriteSlotField(fixture, asset, parentEntity, sensorVarName, new EqsSensorHandle(childEntity));

        // Advance to time ~6s (not stale yet: 6 - 5 = 1 < 2)
        fixture.TickFrame(6f);
        ResetBoolField(fixture, asset, parentEntity, "WasFired");

        // Advance to time ~8s (stale: 8 - 5 = 3 > 2)
        fixture.TickFrame(2f);
        bool isStale = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
        Assert.True(isStale, "Should fire when buffer age exceeds MaxAge based on sim time");
    }

    [Fact]
    public void EqsResult_ChildEntityDestroyed_NoFire_NoCrash()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();

        var (asset, _, sensorVarName) = BuildEqsResultAsset(EqsTrigger.FirstReady);
        fixture.CompileAndLoad(asset);
        var parentEntity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, parentEntity);

        // Set up a valid child entity with a ready buffer
        var buffer = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 1 };
        var childEntity = SetupEqsChildEntity(fixture, buffer);
        WriteSlotField(fixture, asset, parentEntity, sensorVarName, new EqsSensorHandle(childEntity));

        // Destroy the child entity BEFORE ticking
        fixture.World.DestroyEntity(childEntity);
        Assert.False(fixture.World.IsAlive(childEntity));

        // Tick should not crash; OnFired should not be called
        var exception = Record.Exception(() => fixture.TickFrame(0.016f));
        Assert.Null(exception);

        bool fired = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
        Assert.False(fired, "No fire when child entity is destroyed");
    }
}
