using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Modules.Geographic.Components;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Utility;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

[Collection("DebugProbe")]
public sealed class UtilityNodeRuntimeTests
{
    // ---- State reading helper ----

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

    // ---- Helper: register all component types the utility AI scorer may query ----

    private static void RegisterUtilityComponents(BlueprintTestFixture fixture)
    {
        fixture.World.RegisterComponent<Health>();
        fixture.World.RegisterComponent<WeaponState>();
        fixture.World.RegisterComponent<WeaponMountInfo>();
        fixture.World.RegisterComponent<PartMetadata>();
        fixture.World.RegisterComponent<TargetMemory>();
        fixture.World.RegisterComponent<SensorContactList>();
        fixture.World.RegisterComponent<EqsSensor>();
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<UnitRoster>();
        fixture.World.RegisterComponent<UnitSubordinate>();
        fixture.World.RegisterComponent<Blackboard1024>();
        fixture.World.RegisterComponent<Position>();
        fixture.World.RegisterComponent<UtilityDebugFlags>();
        fixture.World.RegisterComponent<UtilityTraceWorkingMemory1024>();
        fixture.World.RegisterComponent<UtilityResultBuffer>();
    }

    // ---- SC-P1-09-3 ----

    [Fact]
    public void ScoreDecisionNode_Produces_WinningOption()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterUtilityComponents(fixture);
        UtilityDecisionCatalog.RegisterAll(out _);
        StandardInputs.RegisterAll();

        // Build asset: ScoreDecisionNode("CombatPosture") -> SetVar("PostureOut" : byte)
        var assetId      = Guid.NewGuid();
        var graphId      = Guid.NewGuid();
        var postureVarId = Guid.NewGuid();

        var postureVar = new VariableDecl
        {
            Id   = postureVarId,
            Name = "PostureOut",
            Type = new BlueprintTypeRef { TypeId = "System.Byte" },
        };

        // ScoreDecisionNode
        var scoreNodeId  = Guid.NewGuid();
        var scoreExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",          Direction = "In",  IsExec = true,  TypeRef = new() };
        var scoreExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut",         Direction = "Out", IsExec = true,  TypeRef = new() };
        var winningPinId = Guid.NewGuid();
        var winningPin   = new Pin { Id = winningPinId,   Name = "WinningOptionId", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Byte" } };
        var scoreNode    = new ScoreDecisionNode { Id = scoreNodeId, AssetId = "3c6f9e42-5d10-6f3a-ac23-posture0000001" };
        scoreNode.Pins.AddRange(new[] { scoreExecIn, scoreExecOut, winningPin });

        // SetVariableNode("PostureOut")
        var setPostureId      = Guid.NewGuid();
        var setPostureExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var setPostureExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
        var setPostureDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
        var setPostureNode    = new SetVariableNode { Id = setPostureId, VariableId = postureVarId.ToString() };
        setPostureNode.Pins.AddRange(new[] { setPostureExecIn, setPostureExecOut, setPostureDataIn });

        // Entry + Return
        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);
        var retNode = new ReturnNode { Id = Guid.NewGuid() };
        var retIn   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = graphId,
            Name  = "Tick",
            Kind  = GraphKind.Function,
            Nodes = { entry, scoreNode, setPostureNode, retNode },
            Links =
            {
                new Link { FromNodeId = entry.Id,     FromPinId = entryExecOut.Id,      ToNodeId = scoreNodeId,  ToPinId = scoreExecIn.Id },
                new Link { FromNodeId = scoreNodeId,  FromPinId = scoreExecOut.Id,      ToNodeId = setPostureId, ToPinId = setPostureExecIn.Id },
                new Link { FromNodeId = setPostureId, FromPinId = setPostureExecOut.Id, ToNodeId = retNode.Id,   ToPinId = retIn.Id },
                // Data: ScoreDecision.WinningOptionId -> SetVar.Value
                new Link { FromNodeId = scoreNodeId, FromPinId = winningPinId, ToNodeId = setPostureId, ToPinId = setPostureDataIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId   = assetId,
            Name      = "ScoreDecisionTest",
            Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Variables = { postureVar },
            Graphs    = { graph },
        };

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new Health    { Current = 100f, Max = 100f });
        fixture.World.AddComponent(entity, new WeaponState { Ammo = 30, MaxAmmo = 30 });
        fixture.World.AddComponent(entity, new TargetMemory());     // empty -> HaveLiveTarget = 0
        fixture.World.AddComponent(entity, new UtilityResultBuffer());
        fixture.AttachBlueprint(asset, entity);
        fixture.TickFrame(0.016f);

        byte postureOut = ReadSlotField<byte>(fixture, asset, entity, "PostureOut");
        // No live targets, no EQS data -> Hold (Constant(0.2f) floor) is the only
        // positive-scoring option; all others multiply out to zero.
        Assert.Equal((byte)Posture.Hold, postureOut);
    }

    // ---- SC-P1-09-4 ----

    [Fact]
    public void ReadRankedResultNode_Reads_TopBufferEntry()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<UtilityResultBuffer>();

        // Build asset: ReadRankedResultNode(Rank=0) -> SetVar("TopEntity" : long),
        //              SetVar("TopScore" : float), SetVar("TopIsValid" : bool)
        var assetId         = Guid.NewGuid();
        var graphId         = Guid.NewGuid();
        var topEntityVarId  = Guid.NewGuid();
        var topScoreVarId   = Guid.NewGuid();
        var topIsValidVarId = Guid.NewGuid();

        var topEntityVar  = new VariableDecl { Id = topEntityVarId,  Name = "TopEntity",  Type = new BlueprintTypeRef { TypeId = "System.Int64"   } };
        var topScoreVar   = new VariableDecl { Id = topScoreVarId,   Name = "TopScore",   Type = new BlueprintTypeRef { TypeId = "System.Single"  } };
        var topIsValidVar = new VariableDecl { Id = topIsValidVarId, Name = "TopIsValid", Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };

        // ReadRankedResultNode (data-only, no exec pins)
        var readNodeId   = Guid.NewGuid();
        var entityPinId  = Guid.NewGuid();
        var scorePinId   = Guid.NewGuid();
        var isValidPinId = Guid.NewGuid();
        var entityOutPin  = new Pin { Id = entityPinId,  Name = "Entity",  Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int64"   } };
        var scoreOutPin   = new Pin { Id = scorePinId,   Name = "Score",   Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Single"  } };
        var isValidOutPin = new Pin { Id = isValidPinId, Name = "IsValid", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var readNode = new ReadRankedResultNode { Id = readNodeId, Rank = 0 };
        readNode.Pins.AddRange(new[] { entityOutPin, scoreOutPin, isValidOutPin });

        // SetVariableNode("TopEntity")
        var setEntityId      = Guid.NewGuid();
        var setEntityExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var setEntityExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
        var setEntityDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
        var setEntityNode    = new SetVariableNode { Id = setEntityId, VariableId = topEntityVarId.ToString() };
        setEntityNode.Pins.AddRange(new[] { setEntityExecIn, setEntityExecOut, setEntityDataIn });

        // SetVariableNode("TopScore")
        var setScoreId      = Guid.NewGuid();
        var setScoreExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var setScoreExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
        var setScoreDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
        var setScoreNode    = new SetVariableNode { Id = setScoreId, VariableId = topScoreVarId.ToString() };
        setScoreNode.Pins.AddRange(new[] { setScoreExecIn, setScoreExecOut, setScoreDataIn });

        // SetVariableNode("TopIsValid")
        var setIsValidId      = Guid.NewGuid();
        var setIsValidExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var setIsValidExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
        var setIsValidDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
        var setIsValidNode    = new SetVariableNode { Id = setIsValidId, VariableId = topIsValidVarId.ToString() };
        setIsValidNode.Pins.AddRange(new[] { setIsValidExecIn, setIsValidExecOut, setIsValidDataIn });

        // Entry + Return
        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);
        var retNode = new ReturnNode { Id = Guid.NewGuid() };
        var retIn   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = graphId,
            Name  = "Tick",
            Kind  = GraphKind.Function,
            Nodes = { entry, readNode, setEntityNode, setScoreNode, setIsValidNode, retNode },
            Links =
            {
                // Exec chain: Entry -> SetVar(TopEntity) -> SetVar(TopScore) -> SetVar(TopIsValid) -> Return
                new Link { FromNodeId = entry.Id,      FromPinId = entryExecOut.Id,      ToNodeId = setEntityId,  ToPinId = setEntityExecIn.Id },
                new Link { FromNodeId = setEntityId,   FromPinId = setEntityExecOut.Id,  ToNodeId = setScoreId,   ToPinId = setScoreExecIn.Id },
                new Link { FromNodeId = setScoreId,    FromPinId = setScoreExecOut.Id,   ToNodeId = setIsValidId, ToPinId = setIsValidExecIn.Id },
                new Link { FromNodeId = setIsValidId,  FromPinId = setIsValidExecOut.Id, ToNodeId = retNode.Id,   ToPinId = retIn.Id },
                // Data: ReadRankedResult outputs -> SetVar value inputs
                new Link { FromNodeId = readNodeId, FromPinId = entityPinId,  ToNodeId = setEntityId,  ToPinId = setEntityDataIn.Id },
                new Link { FromNodeId = readNodeId, FromPinId = scorePinId,   ToNodeId = setScoreId,   ToPinId = setScoreDataIn.Id },
                new Link { FromNodeId = readNodeId, FromPinId = isValidPinId, ToNodeId = setIsValidId, ToPinId = setIsValidDataIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId   = assetId,
            Name      = "ReadRankedResultTest",
            Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Variables = { topEntityVar, topScoreVar, topIsValidVar },
            Graphs    = { graph },
        };

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new UtilityResultBuffer());

        // Pre-seed buffer: slot 0 = candidate 42, score 0.8
        ref var buf = ref fixture.World.GetComponentRW<UtilityResultBuffer>(entity);
        buf.Count = 1;
        buf.GetSpanRW()[0] = new UtilityResultEntry { CandidateHandle = 42L, Score = 0.8f };

        fixture.AttachBlueprint(asset, entity);
        fixture.TickFrame(0.016f);

        long  topEntity  = ReadSlotField<long >(fixture, asset, entity, "TopEntity");
        float topScore   = ReadSlotField<float>(fixture, asset, entity, "TopScore");
        bool  topIsValid = ReadSlotField<bool >(fixture, asset, entity, "TopIsValid");
        Assert.Equal(42L,  topEntity);
        Assert.Equal(0.8f, topScore);
        Assert.True(topIsValid);
    }
}
