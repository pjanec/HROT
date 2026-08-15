using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using AssetDispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b>Batch 56 / ruling 8 — <c>Variable</c> and <c>WorkingState</c> are ONE cell, so they are
/// ONE struct.</b>
///
/// <para>
/// ⭐ <b>The user's words:</b> <i>"as the global vars and working state vars are the same stuff, it
/// makes no sense to emit them differently… no keeping two implementations for the same concept."</i>
/// </para>
///
/// <para>
/// ⛔⛔ <b>The defect these fixtures pin, and it was live.</b> <c>U-12</c> made the mixture legal at
/// Stage 2 — <c>BP1024</c> retired, <c>BP1031</c> split — and <c>Stage5.FindVariableRef</c> already
/// resolved across both kinds. ⚠ <b>Nothing told the emitters.</b> <c>InstanceEmitter</c> walked
/// <c>asset.Variables</c>; <c>AiPrimitiveEmitter</c> walked <c>asset.WorkingState</c>. So a
/// declaration on the "wrong" side either
/// <list type="bullet">
///   <item>— <b>referenced</b>: bound by Stage 5, never emitted ⇒ a <b>Roslyn</b> error naming a field
///   the designer never wrote (<c>BP-228</c>'s shape: a diagnostic in the wrong language), or</item>
///   <item>— 🔴🔴 <b>unreferenced</b>: <b>silently absent at runtime.</b> Declared, initial value
///   authored, and it simply does not exist.</item>
/// </list>
/// </para>
///
/// <para>
/// ⭐⭐ <b>Why no gate could have caught it.</b> Measured over all 458 shipped <c>.bp.json</c>:
/// <b>0 carry both kinds</b> — 193 are <c>(Variable)</c>, 32 are <c>(Parameter, WorkingState)</c>.
/// ⇒ the union is a <b>no-op on the whole corpus</b>, which is why <c>StructureHash</c> and the golden
/// tiers must not move, and why <b>these assets have to be constructed</b>: <c>BP-240</c>'s shape, a
/// rail relaxed without telling the code it was protecting.
/// </para>
///
/// <para>
/// ⚠ <b>Every fixture below drives the FULL pipeline through real Roslyn</b>
/// (<see cref="BlueprintTestFixture.CompileAndLoad"/>) and then reads the <b>loaded</b> type's layout.
/// ⛔ Asserting on the emitted string would have been satisfied by a field the C# compiler then
/// rejected; the reflection is what makes "it compiles and runs" the claim.
/// </para>
/// </summary>
public sealed class StateTierUnificationTests
{
    // ── graph construction ──────────────────────────────────────────────────
    //
    // ⚠ Hand-built rather than via GraphBuilder: its `SetVariable` carries no value pin, so it cannot
    //   express a WIRED read→write, and an unwired read is eliminated by Stage 3 as an orphan. This is
    //   `VariableKindResolutionTests.EmitReadWrite`'s shape, driven through the real compiler instead.

    private static Pin ExecIn()  => new() { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true };
    private static Pin ExecOut() => new() { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true };

    private static Pin DataPin(string name, string direction) => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false,
        TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" },
    };

    /// <summary>Adds the asset's main graph: <c>Entry → Set(write) ← Get(read) → Return</c>.</summary>
    private static void AddReadWriteGraph(BlueprintAsset asset, Guid readTarget, Guid writeTarget)
    {
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecOut(); entry.Pins.Add(entryOut);

        var get = new GetVariableNode { Id = Guid.NewGuid(), VariableId = readTarget.ToString() };
        var getOut = DataPin("Value", "Out"); get.Pins.Add(getOut);

        var set = new SetVariableNode { Id = Guid.NewGuid(), VariableId = writeTarget.ToString() };
        var setIn = ExecIn(); var setOut = ExecOut(); var setVal = DataPin("Value", "In");
        set.Pins.AddRange(new[] { setIn, setOut, setVal });

        var ret = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        var retIn = ExecIn(); ret.Pins.Add(retIn);

        asset.Graphs.Add(new Graph
        {
            Id   = Guid.NewGuid(),
            Name = asset.Dispatch == AssetDispatch.AiPrimitive ? "Main" : "Tick",
            Kind = GraphKind.Function,
            Nodes = { entry, get, set, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = set.Id, ToPinId = setIn.Id },
                new Link { FromNodeId = get.Id,   FromPinId = getOut.Id,   ToNodeId = set.Id, ToPinId = setVal.Id },
                new Link { FromNodeId = set.Id,   FromPinId = setOut.Id,   ToNodeId = ret.Id, ToPinId = retIn.Id },
            },
        });
    }

    private static void AddEmptyGraph(BlueprintAsset asset)
    {
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecOut(); entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        var retIn = ExecIn(); ret.Pins.Add(retIn);

        asset.Graphs.Add(new Graph
        {
            Id   = Guid.NewGuid(),
            Name = asset.Dispatch == AssetDispatch.AiPrimitive ? "Main" : "Tick",
            Kind = GraphKind.Function,
            Nodes = { entry, ret },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = ret.Id, ToPinId = retIn.Id } },
        });
    }

    // ── reflection helpers ──────────────────────────────────────────────────

    private static Type GeneratedClass(Assembly asm, string stateStructName)
        => asm.GetTypes().Single(t => t.Name.EndsWith("_Bp", StringComparison.Ordinal)
                                   && t.GetNestedType(stateStructName) is not null);

    private delegate void SpanAction(Span<byte> bytes);

    /// <summary>
    /// Runs the GENERATED <c>InitDefault</c> over a zeroed buffer and returns it. ⭐ This is the
    /// initialiser the runtime actually calls on a hash-mismatch re-init — not a re-implementation.
    /// </summary>
    private static byte[] RunInitDefault(Type bpClass, Type stateType)
    {
        var bytes = new byte[Marshal.SizeOf(stateType) + 64];
        var init  = (SpanAction)Delegate.CreateDelegate(typeof(SpanAction), bpClass.GetMethod("InitDefault")!);
        init(bytes);
        return bytes;
    }

    private static int ReadInt(byte[] bytes, Type stateType, string field)
        => BitConverter.ToInt32(bytes, (int)Marshal.OffsetOf(stateType, field));

    // ────────────────────────────────────────────────────────────────────────
    // 1 — an Instance declaring WorkingState, REFERENCED
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>An Instance may declare <c>WorkingState</c> since <c>U-12</c> split <c>BP1031</c>, and a
    /// reference to it must reach a real field.</b>
    ///
    /// <para>
    /// ⛔ <b>RED before:</b> Stage 5 bound the reference and emitted <c>s.W1 = …</c>, but
    /// <c>EmitStateStruct</c> walked <c>Variables</c> only, so the struct had no such member and
    /// <see cref="BlueprintTestFixture.CompileAndLoad"/> threw on the Roslyn error — an error naming a
    /// generated file rather than the declaration.
    /// </para>
    /// </summary>
    [Fact]
    public void AnInstanceWorkingStateDeclaration_IsAFieldOfState_AndIsReferenceable()
    {
        var asset = BlueprintAssetBuilder.Instance("InstanceWithWorkingState")
            .WithWorkingStateField("W0", typeof(int))
            .WithWorkingStateField("W1", typeof(int))
            .Build();

        AddReadWriteGraph(asset,
            readTarget:  asset.WorkingState.Single(f => f.Name == "W0").Id,
            writeTarget: asset.WorkingState.Single(f => f.Name == "W1").Id);

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asm = fixture.CompileAndLoad(asset);

        var state = GeneratedClass(asm, "State").GetNestedType("State")!;
        Assert.NotNull(state.GetField("W0"));
        Assert.NotNull(state.GetField("W1"));
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2 — an AiPrimitive declaring Variable, REFERENCED
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The mirror image: an AiPrimitive may declare a <c>Variable</c> since <c>U-12</c> retired
    /// <c>BP1024</c>.</b> ⚠ The struct is still <b>called</b> <c>WorkingState</c> — that name is ABI and
    /// <c>InlineActionLowering</c> emits it literally. ⭐ Ruling 8 unifies what the emitters WALK, not
    /// what the structs are CALLED.
    /// </summary>
    [Fact]
    public void AnAiPrimitiveVariableDeclaration_IsAFieldOfWorkingState_AndIsReferenceable()
    {
        var asset = BlueprintAssetBuilder.AiPrimitive("PrimitiveWithVariables")
            .WithHostings(AiPrimitiveHosting.BlueprintCall)
            .WithVariable("V0", typeof(int))
            .WithVariable("V1", typeof(int))
            .Build();

        AddReadWriteGraph(asset,
            readTarget:  asset.Variables.Single(f => f.Name == "V0").Id,
            writeTarget: asset.Variables.Single(f => f.Name == "V1").Id);

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asm = fixture.CompileAndLoad(asset);

        var ws = GeneratedClass(asm, "WorkingState").GetNestedType("WorkingState")!;
        Assert.NotNull(ws.GetField("V0"));
        Assert.NotNull(ws.GetField("V1"));
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3 — 🔴 THE SILENT CASE: unreferenced, with an initial value
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴🔴 <b>The case with no symptom at all.</b> An unreferenced wrong-side declaration produced no
    /// Roslyn error — there was nothing to name it. It was declared, its initial value was authored and
    /// persisted, and at runtime <b>it did not exist</b>.
    ///
    /// <para>
    /// ⭐ Asserted through the GENERATED <c>InitDefault</c> over real bytes, at the offset
    /// <c>Marshal.OffsetOf</c> reports for the loaded struct — ⛔ not "a descriptor exists".
    /// <b>RED before:</b> <c>OffsetOf</c> throws, because the field is not in the struct.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnreferencedWorkingStateDeclarationOnAnInstance_ExistsAndKeepsItsInitialValue()
    {
        var asset = BlueprintAssetBuilder.Instance("InstanceSilentScratch")
            .WithWorkingStateField("Scratch", typeof(int))
            .Build();
        asset.WorkingState.Single(f => f.Name == "Scratch").DefaultValueJson = "7";
        AddEmptyGraph(asset);

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asm = fixture.CompileAndLoad(asset);

        var bpClass = GeneratedClass(asm, "State");
        var state   = bpClass.GetNestedType("State")!;

        Assert.NotNull(state.GetField("Scratch"));
        Assert.Equal(7, ReadInt(RunInitDefault(bpClass, state), state, "Scratch"));
    }

    /// <summary>
    /// 🔴 The same silent case on the AiPrimitive side, through the generated
    /// <c>InitDefaultWorkingState</c> — the initialiser the emitted thunks run on a hash mismatch.
    /// <para>
    /// ⚠ Invoked by reflection through a boxed pointer because the generated method is
    /// <c>private static unsafe void(WorkingState*)</c>. ⭐ Worth the awkwardness: re-implementing the
    /// initialisation here would test this test, not the emitter.
    /// </para>
    /// </summary>
    [Fact]
    public unsafe void AnUnreferencedVariableOnAnAiPrimitive_ExistsAndKeepsItsInitialValue()
    {
        var asset = BlueprintAssetBuilder.AiPrimitive("PrimitiveSilentScratch")
            .WithHostings(AiPrimitiveHosting.BlueprintCall)
            .WithVariable("Scratch", typeof(int), "9")
            .Build();
        AddEmptyGraph(asset);

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asm = fixture.CompileAndLoad(asset);

        var bpClass = GeneratedClass(asm, "WorkingState");
        var ws      = bpClass.GetNestedType("WorkingState")!;
        Assert.NotNull(ws.GetField("Scratch"));

        var init = bpClass.GetMethod(
            "InitDefaultWorkingState", BindingFlags.NonPublic | BindingFlags.Static)!;
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf(ws));
        try
        {
            init.Invoke(null, new object?[] { Pointer.Box((void*)buffer, ws.MakePointerType()) });
            Assert.Equal(9, Marshal.ReadInt32(buffer + (int)Marshal.OffsetOf(ws, "Scratch")));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // 4 — both kinds in one asset
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Both kinds, one asset, an initial value on each — and the declared order holds.</b>
    ///
    /// <para>
    /// ⚠ <b>The order is <c>DeclarationList.KindOrder</c>: WorkingState, then Variable.</b> That is
    /// storage order — the order the store keeps its runs in and the order
    /// <c>StructureHashComputation</c> appends — ⛔ <b>not</b> resolution order (Variables first), which
    /// is a name-collision <i>priority</i>. ⭐ Asserted through real offsets, so the emitted struct and
    /// the hashed layout cannot drift apart silently.
    /// </para>
    /// </summary>
    [Fact]
    public void BothKindsInOneAsset_BothSurvive_InStorageOrder_WithTheirInitialValues()
    {
        var asset = BlueprintAssetBuilder.Instance("InstanceBothKinds")
            .WithWorkingStateField("Wa", typeof(int))
            .WithVariable("Vb", typeof(int), "5")
            .Build();
        asset.WorkingState.Single(f => f.Name == "Wa").DefaultValueJson = "3";
        AddEmptyGraph(asset);

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asm = fixture.CompileAndLoad(asset);

        var bpClass = GeneratedClass(asm, "State");
        var state   = bpClass.GetNestedType("State")!;

        var wa = (int)Marshal.OffsetOf(state, "Wa");
        var vb = (int)Marshal.OffsetOf(state, "Vb");
        Assert.True(wa < vb,
            $"WorkingState must precede Variable in the state struct (KindOrder); got Wa@{wa}, Vb@{vb}.");

        var bytes = RunInitDefault(bpClass, state);
        Assert.Equal(3, ReadInt(bytes, state, "Wa"));
        Assert.Equal(5, ReadInt(bytes, state, "Vb"));

        // ⭐ And the runtime descriptors describe the same struct: `BP-223`'s lesson is that a producer
        //   is not the deliverable — the consumer has to resolve it.
        Assert.True(fixture.Registry.TryGetById(BlueprintIdHash.Compute(asset.AssetId), out var def));
        Assert.True(def!.StateFields.ContainsKey("Wa"));
        Assert.True(def.StateFields.ContainsKey("Vb"));
        Assert.Equal(wa, def.StateFields["Wa"].OffsetBytes);
        Assert.Equal(vb, def.StateFields["Vb"].OffsetBytes);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 5 — the rail that must NOT have moved
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b><c>BP1673</c> still refuses a cross-kind name collision.</b>
    ///
    /// <para>
    /// ⚠ <b>Not a red-first fixture — a regression guard, deliberately.</b> Unifying the emitters makes
    /// two same-named declarations land in ONE struct, where they would be a Roslyn <c>CS0102</c>
    /// instead of a blueprint diagnostic. ⛔ That rail existing is the reason this batch does not need
    /// to invent one; ⭐ it being still ARMED is the thing worth asserting.
    /// </para>
    /// </summary>
    [Fact]
    public void ACrossKindNameCollision_IsStillRefusedByBP1673()
    {
        var asset = BlueprintAssetBuilder.Instance("InstanceCollidingNames")
            .WithWorkingStateField("Dup", typeof(int))
            .WithVariable("Dup", typeof(int))
            .Build();
        AddEmptyGraph(asset);

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>())));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1673);
    }
}
