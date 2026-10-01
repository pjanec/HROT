using System;
using System.Collections.Generic;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-384</c> — an HSM asset can ADDRESS a blueprint-hosted activity and guard.</b>
/// 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.2, §9 ④.
///
/// <para>🔴 <b>The gap.</b> An asset names actions by string and <c>HsmFlattener</c> turns that into
/// <c>FNV1a16(FQN)</c>; a blueprint-hosted thunk registers under <c>(ushort)BlueprintId</c>, which is
/// FNV-1a32 of the asset GUID. ⛔ <b>No authorable string bridges those id spaces</b>, so the id has
/// to be BAKED from the <c>.bp.json</c> — and it cannot be looked up by symbol, because sibling
/// Roslyn generators cannot see each other's output.</para>
///
/// <para>⚠ <b>These rails test the EMITTER, which is the seam that bakes the id.</b> The end-to-end
/// "the blob addresses what the registrar registers" claim needs the blueprint compiler in the loop
/// and is <c>CE-385</c>/<c>CE-386</c>'s to make once an ASSET can carry the reference through the
/// editor. ⛔ Said plainly so the coverage here is not mistaken for the whole story.</para>
/// </summary>
public sealed class HsmBlueprintActionIdTests
{
    private static readonly Guid BpAsset = new("b7000001-0000-0000-0000-00000000abcd");
    private const ushort BakedId = 0x4242;

    /// <summary>The resolver the generator hands in — here a stub, so the rail tests the EMITTER
    /// rather than the catalog's parsing (which has its own coverage).</summary>
    private static Func<Guid, ushort?> Resolver(params Guid[] known)
        => id => Array.IndexOf(known, id) >= 0 ? BakedId : (ushort?)null;

    private static StateNodeDto StateA(HsmAssetDto dto)
        => dto.States.Find(s => s.Name == "A")!;

    private static HsmAssetDto TwoStateAsset(
        Guid? activityBlueprint = null, Guid? guardBlueprint = null, bool polled = false)
    {
        var root = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
        var a = Guid.Parse("aa000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("bb000000-0000-0000-0000-000000000002");
        // ⚠ FIXTURE SHAPE, learned the hard way: the emitter finds the COMPILER ROOT as the state
        //   with no resolvable parent, and emits ITS CHILDREN as top level. A flat two-state list
        //   with no __Root therefore emits an EMPTY builder — which is what the first draft of this
        //   file measured. The shipped assets all carry this shape; the fixture must too.
        return new HsmAssetDto
        {
            AssetId = Guid.Parse("11110000-0000-0000-0000-000000000001"),
            Name = "BpIdDemo",
            TargetNamespace = "Demo.Machines",
            States = new List<StateNodeDto>
            {
                new() { StableId = root, Name = "__Root",
                        ChildStableIds = new List<Guid> { a, b } },
                new() { StableId = a, Name = "A", IsInitial = true, ParentStableId = root,
                        Activity = activityBlueprint is Guid ab && ab != Guid.Empty
                            ? new BehaviorActionBindingDto { BlueprintAssetId = ab } : null },
                new() { StableId = b, Name = "B", ParentStableId = root },
            },
            Transitions = new List<TransitionNodeDto>
            {
                new() { VisualId = Guid.Parse("cc000000-0000-0000-0000-000000000003"),
                        SourceStableId = a, TargetStableId = b,
                        Guard = guardBlueprint is Guid gb && gb != Guid.Empty
                            ? new BehaviorActionBindingDto { BlueprintAssetId = gb } : null,
                        IsPolled = polled },
            },
        };
    }

    // ── the baked id reaches the emitted builder ─────────────────────────────────────

    [Fact]
    public void ABlueprintActivity_EmitsAnExplicitActivityId()
    {
        string src = HsmEmitCore.EmitTopologyCore(
            TwoStateAsset(activityBlueprint: BpAsset), null, Resolver(BpAsset));

        src.Should().Contain($".ActivityId({BakedId})",
            "the blueprint's id must be BAKED — no authorable name hashes to it");
    }

    [Fact]
    public void ABlueprintGuard_EmitsAnExplicitGuardId()
    {
        string src = HsmEmitCore.EmitTopologyCore(
            TwoStateAsset(guardBlueprint: BpAsset), null, Resolver(BpAsset));

        src.Should().Contain($".GuardId({BakedId})");
    }

    /// <summary>
    /// ⛔⛔ <b>An UNRESOLVED reference emits NOTHING — it must never fall back to <c>0</c>.</b>
    /// ⭐ <c>0</c> is a VALID action id, so a fallback would dispatch something arbitrary instead of
    /// failing: exactly the silent <c>TryGetValue</c> miss <c>E6</c> spent a batch on.
    /// </summary>
    [Fact]
    public void AnUnresolvedBlueprintReference_EmitsNoIdAtAll_NeverZero()
    {
        string src = HsmEmitCore.EmitTopologyCore(
            TwoStateAsset(activityBlueprint: BpAsset), null, Resolver(/* knows nothing */));

        src.Should().NotContain(".ActivityId(");
        src.Should().NotContain(".ActivityId(0)");
    }

    /// <summary>⚠ No resolver at all — the editor/test path — must also emit nothing rather than throw.</summary>
    [Fact]
    public void WithNoResolver_ABlueprintReferenceIsSimplyNotEmitted()
    {
        string src = HsmEmitCore.EmitTopologyCore(TwoStateAsset(activityBlueprint: BpAsset), null);
        src.Should().NotContain(".ActivityId(");
    }

    // ── compatibility: an asset naming no blueprint is untouched ─────────────────────

    /// <summary>
    /// ⭐⭐ <b>The no-churn property, asserted rather than hoped.</b> An asset with no blueprint
    /// reference must emit BYTE-IDENTICALLY with and without a resolver — otherwise every shipped
    /// asset's golden moves the day this feature lands.
    /// </summary>
    [Fact]
    public void AnAssetWithNoBlueprintReference_EmitsIdenticallyWithAndWithoutAResolver()
    {
        var dto = TwoStateAsset();
        HsmEmitCore.EmitTopologyCore(dto, null, Resolver(BpAsset))
            .Should().Be(HsmEmitCore.EmitTopologyCore(dto, null));
    }

    // ── CE-381's marker reaches the emitted builder ──────────────────────────────────

    [Fact]
    public void APolledTransition_EmitsThePolledMarker()
    {
        string src = HsmEmitCore.EmitTopologyCore(TwoStateAsset(polled: true), null);
        src.Should().Contain(".Polled()");
    }

    [Fact]
    public void AnUnmarkedTransition_EmitsNoPolledMarker()
    {
        string src = HsmEmitCore.EmitTopologyCore(TwoStateAsset(polled: false), null);
        src.Should().NotContain(".Polled()");
    }

    // ── the DTO round-trips ──────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The four new fields survive serialize → deserialize, and ⛔ an asset that sets none of them
    /// serialises WITHOUT them — which is what keeps the four shipped assets' canonical JSON
    /// unchanged (they are all <c>WhenWritingDefault</c>).
    /// </summary>
    [Fact]
    public void TheNewFieldsRoundTrip_AndAreAbsentWhenUnset()
    {
        var dto = TwoStateAsset(activityBlueprint: BpAsset, guardBlueprint: BpAsset, polled: true);
        // ⚠ By NAME, not by index: States[0] is __Root (see the fixture note above).
        StateA(dto).Activity!.BlueprintName = "SomeBlueprint";
        dto.Transitions[0].Guard!.BlueprintName = "SomeGuard";

        var back = HsmJsonServices.Deserialize(HsmJsonServices.Serialize(dto))!;

        StateA(back).Activity!.BlueprintAssetId.Should().Be(BpAsset);
        StateA(back).Activity!.BlueprintName.Should().Be("SomeBlueprint");
        back.Transitions[0].Guard!.BlueprintAssetId.Should().Be(BpAsset);
        back.Transitions[0].Guard!.BlueprintName.Should().Be("SomeGuard");
        back.Transitions[0].IsPolled.Should().BeTrue();

        string bare = HsmJsonServices.Serialize(TwoStateAsset());
        bare.Should().NotContain("BlueprintAssetId");   // CE-417: no blueprint binding is written for an unset slot
        bare.Should().NotContain("IsPolled");
    }
}
