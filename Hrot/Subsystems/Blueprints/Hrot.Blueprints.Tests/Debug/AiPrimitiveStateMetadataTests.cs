using System.Linq;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Tests.Builders;
using RuntimeDispatchKind = Fdp.Toolkit.Blueprints.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// ⭐⭐⭐ <b>Batch 57 (<c>S1</c>) — a whole dispatch kind was invisible to the debugger.</b>
///
/// <para>
/// ⛔⛔ <b>The gap, measured by reading rather than inferred.</b> <c>CSharpEmitter</c> gated the
/// variable pins <i>and</i> <c>AddStateLayoutField</c> on <c>Dispatch == Instance</c>, and
/// <c>EmitAiPrimitiveRegistration</c> wrote <c>StateSize = 0</c> with <b>no <c>StateFields</c> block at
/// all</b>. ⇒ <c>BlueprintDefinition.StateFields</c> was empty and <c>DebugMap.StateLayout</c> was empty
/// for <b>every</b> AiPrimitive asset — <b>32 of the shipped corpus</b>, not a corner.
/// </para>
///
/// <para>
/// ⭐⭐ <b>The sharpest part: the reader already existed.</b>
/// <c>BlueprintDebugSession.CaptureAiPrimitiveState</c> is written, shipped, and named for exactly this
/// case — it validates <c>storedHash == def.StructureHash</c>, then reads
/// <c>mapIndex?.StateLayout.Fields</c> <b>or</b> <c>def.StateFields</c>. ⛔ Both were empty, so it
/// silently read nothing and returned. ⚠ <b>A consumer with no producer</b>, green for its entire life
/// because nothing ever asked it for a value.
/// </para>
///
/// <para>
/// ⭐ <b>These tests assert the VALUE, not the descriptor</b> (<c>BP-223</c>'s lesson: verify the
/// consumer). They drive the real generated <c>HsmActivity</c> thunk, which writes the structure hash
/// into <c>Blackboard1024</c> and runs the generated <c>InitDefaultWorkingState</c> — so the bytes
/// being read back are the ones the runtime actually wrote.
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class AiPrimitiveStateMetadataTests
{
    private static BlueprintTestFixtureOptions NoAlcCheck { get; } =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    private static CompileOptions Options() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: System.Array.Empty<BlueprintSignature>());

    /// <summary>An HSM-hosted AiPrimitive — the hosting whose thunk initialises <c>Blackboard1024</c>.</summary>
    private static BlueprintAssetBuilder Primitive(string name)
        => BlueprintAssetBuilder
            .AiPrimitive(name)
            .WithHostings(AiPrimitiveHosting.HsmAction)
            .WithGraph("Main", g => g.Entry().Return());

    // ────────────────────────────────────────────────────────────────────────
    // ⭐⭐⭐ the gate that matters — the value comes back
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>RED before:</b> <c>def.StateFields</c> was empty, so the snapshot came back with <b>no
    /// fields at all</b> — no exception, no log, an empty dictionary. ⭐ Green means the whole chain
    /// works: emitter → registrar → <c>BlueprintDefinition</c> → <c>CaptureAiPrimitiveState</c> →
    /// <c>MarshalFromBytes</c> → a value the panel can print.
    /// </summary>
    [Fact]
    public void AnAiPrimitivesWorkingStateValues_ComeBackThroughTheDebugSession()
    {
        var asset = Primitive("AiPrimStateReadback")
            .WithWorkingStateField("Ticks", typeof(int))
            .WithWorkingStateField("Ratio", typeof(float))
            .Build();
        asset.WorkingState.Single(f => f.Name == "Ticks").DefaultValueJson = "42";
        // ⚠ `1`, not `0.5`: `Stage5:107` assigns `DefaultValueCSharp = DefaultValueJson` VERBATIM, so a
        //   fractional default emits a bare `0.5` — a C# `double` literal — and the generated assignment
        //   fails with CS0664. 🔴 A real latent defect (filed separately; no shipped asset has a
        //   fractional default, which is why it has never fired) and deliberately NOT this batch's.
        //   An integral literal still exercises the float field end to end.
        asset.WorkingState.Single(f => f.Name == "Ratio").DefaultValueJson = "1";

        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        fixture.CompileAndLoad(asset, Options());

        var entity = fixture.CreateEntity();
        // ⭐ The real thunk: stamps StructureHash at Blackboard1024[0..8) and runs the generated
        //   InitDefaultWorkingState at memory+8. Nothing here writes the bytes by hand.
        fixture.InvokeHsmAction(asset, entity);

        var session  = new BlueprintDebugSession(fixture.Registry, fixture.View, new MockTimeController());
        var snapshot = session.CaptureLiveState(entity, asset.AssetId);

        Assert.NotNull(snapshot);
        Assert.Equal(RuntimeDispatchKind.AiPrimitive, snapshot!.Dispatch);

        Assert.True(snapshot.FieldValues.ContainsKey("Ticks"),
            "the working-state field did not come back at all — StateFields/StateLayout are empty, "
            + "which is exactly the S1 gap: a consumer with no producer.");
        Assert.Equal(42, snapshot.FieldValues["Ticks"]);
        Assert.Equal(1f, snapshot.FieldValues["Ratio"]);
    }

    /// <summary>
    /// ⭐⭐ <b>The offsets are right, not merely present.</b> ⚠ A single field would have passed at
    /// offset 0 by luck; the point of this one is the SECOND and THIRD fields, whose offsets depend on
    /// the <c>-8</c> rebase (<c>FieldLayout</c> lays an AiPrimitive's state out from <b>8</b>, which is
    /// its position in <c>Blackboard1024</c> and <b>not</b> a struct offset, while
    /// <c>CaptureAiPrimitiveState</c> already reads at <c>8 + OffsetBytes</c>).
    /// ⛔ Get that wrong and every field reads the neighbouring one's bytes — plausible values from the
    /// wrong place.
    /// </summary>
    [Fact]
    public void EveryFieldReadsItsOwnBytes_NotItsNeighbours()
    {
        var asset = Primitive("AiPrimOffsets")
            .WithWorkingStateField("A", typeof(int))
            .WithWorkingStateField("B", typeof(int))
            .WithWorkingStateField("C", typeof(long))
            .WithWorkingStateField("D", typeof(byte))
            .Build();
        asset.WorkingState.Single(f => f.Name == "A").DefaultValueJson = "11";
        asset.WorkingState.Single(f => f.Name == "B").DefaultValueJson = "22";
        asset.WorkingState.Single(f => f.Name == "C").DefaultValueJson = "33";
        asset.WorkingState.Single(f => f.Name == "D").DefaultValueJson = "44";

        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        fixture.CompileAndLoad(asset, Options());

        var entity = fixture.CreateEntity();
        fixture.InvokeHsmAction(asset, entity);

        var session  = new BlueprintDebugSession(fixture.Registry, fixture.View, new MockTimeController());
        var snapshot = session.CaptureLiveState(entity, asset.AssetId);

        Assert.NotNull(snapshot);
        Assert.Equal(11,       snapshot!.FieldValues["A"]);
        Assert.Equal(22,       snapshot.FieldValues["B"]);
        Assert.Equal(33L,      snapshot.FieldValues["C"]);
        Assert.Equal((byte)44, snapshot.FieldValues["D"]);
    }

    /// <summary>
    /// ⭐ <b>The descriptors describe the struct, and the first one starts at 0.</b>
    /// ⚠ Asserted directly because it is the half a value read-back cannot distinguish: an off-by-8 in
    /// BOTH the descriptor and the reader would cancel out and still read the right bytes for field 0.
    /// </summary>
    [Fact]
    public void TheDescriptorsAreStructRelative_NotBlackboardRelative()
    {
        var asset = Primitive("AiPrimDescriptorBase")
            .WithWorkingStateField("First",  typeof(int))
            .WithWorkingStateField("Second", typeof(int))
            .Build();

        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        fixture.CompileAndLoad(asset, Options());

        Assert.True(fixture.Registry.TryGetById(BlueprintIdHash.Compute(asset.AssetId), out var def));
        Assert.NotNull(def);

        Assert.True(def!.StateFields.Count >= 2,
            "an AiPrimitive registration carried NO StateFields block at all before S1");
        Assert.Equal(0, def.StateFields["First"].OffsetBytes);
        Assert.Equal(4, def.StateFields["Second"].OffsetBytes);

        // ⭐ And StateSize stopped being the literal 0 it used to be — the working state is real bytes.
        Assert.True(def.StateSize > 0, "StateSize was a hard-coded 0 for every AiPrimitive before S1");
    }

    /// <summary>
    /// 📐 <b>A struct-typed working-state variable</b> — the handoff's explicit ask.
    /// <c>MemberSlotList</c> is a curated blittable struct (96 bytes, <c>StaticTypeRegistry</c>) and
    /// ships in <c>HillAssault2_*</c>.
    ///
    /// <para>
    /// ⭐ <b>What this batch owns is the OFFSET and SIZE</b>, and they are asserted. ⚠ Whether
    /// <c>MarshalFromBytes</c> can render the struct into a value is <c>S3</c>'s arm and explicitly
    /// <b>not</b> this batch — so the scalar declared AFTER the struct is what proves the descriptor
    /// arithmetic survived a 96-byte field, whatever the renderer does with the struct itself.
    /// </para>
    /// </summary>
    [Fact]
    public void AStructTypedWorkingStateField_GetsTheRightOffsetAndSize()
    {
        var asset = Primitive("AiPrimStructField")
            .WithWorkingStateField("Runners", typeof(Hrot.AI.Behaviors.Brains.MemberSlotList))
            .WithWorkingStateField("AfterTheStruct", typeof(int))
            .Build();
        asset.WorkingState.Single(f => f.Name == "AfterTheStruct").DefaultValueJson = "77";

        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        fixture.CompileAndLoad(asset, Options());

        Assert.True(fixture.Registry.TryGetById(BlueprintIdHash.Compute(asset.AssetId), out var def));
        var runners = def!.StateFields["Runners"];
        Assert.Equal(0,  runners.OffsetBytes);
        Assert.Equal(96, runners.SizeBytes);
        Assert.Equal(96, def.StateFields["AfterTheStruct"].OffsetBytes);

        // ⭐ And the scalar past the struct really reads its own bytes at run time.
        var entity = fixture.CreateEntity();
        fixture.InvokeHsmAction(asset, entity);

        var session  = new BlueprintDebugSession(fixture.Registry, fixture.View, new MockTimeController());
        var snapshot = session.CaptureLiveState(entity, asset.AssetId);

        Assert.NotNull(snapshot);
        Assert.Equal(77, snapshot!.FieldValues["AfterTheStruct"]);
    }

    /// <summary>
    /// ⭐ <b>An Instance asset's values still come back</b> — the case that already worked. ⚠ Without
    /// this, "make the AiPrimitive path work" could be satisfied by breaking the one that did:
    /// <c>EmitStateFieldsBlock</c> is now shared between the two, and the offset rebase is conditional
    /// on dispatch kind.
    /// </summary>
    [Fact]
    public void AnInstanceAssetsStateFields_AreUnchanged()
    {
        var asset = BlueprintAssetBuilder.Instance("InstanceStillFine")
            .WithVariable("Alpha", typeof(int), "5")
            .WithVariable("Beta",  typeof(int), "6")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        fixture.CompileAndLoad(asset, Options());

        Assert.True(fixture.Registry.TryGetById(BlueprintIdHash.Compute(asset.AssetId), out var def));

        // ⚠ 16, not 0: an Instance's State opens with a 16-byte BlueprintLatentCursor, so 16 IS the
        //   struct-relative offset of the first variable. ⛔ The AiPrimitive rebase must not reach here.
        Assert.Equal(16, def!.StateFields["Alpha"].OffsetBytes);
        Assert.Equal(20, def.StateFields["Beta"].OffsetBytes);
    }
}
