using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// ⭐⭐⭐ <b>The Instance params seam — <c>DESIGN_Parameter_Model.md</c> §3.3.</b>
///
/// <para>
/// 📌 <b>The user ruling this implements:</b> <i>"Instances could and should reuse the param parsing
/// and resolving."</i> ⇒ the payload becomes <c>[BlueprintLatentCursor 16][Params N][State M]</c>, the
/// attach carries the JSON, and <c>ParseParamsDelegate</c> is reused verbatim — only the destination
/// pointer differs between a behaviour and an Instance.
/// </para>
///
/// <para>
/// ⛔ <c>BlueprintAssignmentDto.Overrides</c> is <b>NOT</b> the mechanism and stays unread; the rail
/// for that is <see cref="ExactlyOneParameterSupplyPathExists"/>.
/// </para>
///
/// <para>
/// ⭐ These are §8's rails, not invented ones: <b>cursor is not overwritten</b> ·
/// <b>parse-before-commit</b> · <b>one supply mechanism</b> · <b>the tail is untouched</b>.
/// </para>
/// </summary>
public sealed unsafe class InstanceParamsSeamTests : IDisposable
{
    // ── a fake Instance that DOES carry parameters ──────────────────────────
    //
    // ⚠ It has to be hand-built: 296 shipped Instance assets carry ZERO parameters, which is exactly
    //   why the `startOffset: 0` trap was invisible. The fixture is the first Instance in the
    //   programme whose params region is non-empty.

    private const int CursorSize  = 16;   // sizeof(BlueprintLatentCursor)
    private const int ParamsSize  = 8;    // ParamsShape
    private const int TickOffset  = CursorSize + ParamsSize;
    private const int PayloadSize = TickOffset + 4;

    private const int DefaultSpeed = 7;
    private const float DefaultRange = 1.5f;

    [StructLayout(LayoutKind.Sequential)]
    private struct ParamsShape
    {
        public int   Speed;
        public float Range;
    }

    private static readonly Guid AssetGuid = new("BEEF0001-0000-0000-0000-000000000070");
    private static readonly int  BpId      = BlueprintIdHash.Compute(AssetGuid);

    /// <summary>
    /// The generated shape, by hand: bake the declared defaults, then overlay a wrapper object keyed
    /// by parameter name. ⭐ Unknown key ignored, malformed JSON throws — <c>DEBT-AIB-021</c>'s decided
    /// behaviours, which this seam reuses rather than redeciding.
    /// </summary>
    private static void ParseParams(
        string json, byte* memory, EntityRepository world, Entity self,
        Fdp.Toolkit.Behavior.IHostVariableAccess? host)
    {
        ref var p = ref Unsafe.AsRef<ParamsShape>(memory);
        p = default;
        p.Speed = DefaultSpeed;
        p.Range = DefaultRange;

        if (string.IsNullOrWhiteSpace(json)) return;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "Speed": p.Speed = prop.Value.GetInt32();   break;
                case "Range": p.Range = prop.Value.GetSingle();  break;
                default: break;   // ⭐ unknown key: ignored
            }
        }
    }

    private static BlueprintDefinition MakeDefinition(bool withParams = true) => new()
    {
        Name          = "FakeParamInstance",
        Kind          = BlueprintDispatchKind.Instance,
        StructureHash = 0x7000_0000_0000_0001UL,
        StateSize     = PayloadSize,
        // ⛔ InitDefault covers the WHOLE payload — including the params region. That is why the
        //    order in AttachToEntity is InitDefault FIRST and the params copy SECOND.
        // ⭐ It stamps a recognisable CURSOR rather than clearing, so the `startOffset: 0` rail can
        //    watch the real attach path: if the params landed at 0 they would erase this pattern, and
        //    a plain Clear() would leave the two cases indistinguishable (zeroes either way).
        InitDefault   = span =>
        {
            span.Clear();
            for (int i = 0; i < CursorSize; i++) span[i] = (byte)(0xA0 + i);
        },
        ParamsOffset  = CursorSize,
        ParamsSize    = withParams ? ParamsSize : 0,
        ParseParams   = withParams ? ParseParams : null,
    };

    private readonly BlueprintRegistry _registry = new();
    private readonly EntityRepository  _repo     = new();

    public InstanceParamsSeamTests()
    {
        _repo.RegisterComponent<BlueprintBlackboard1024>();
        _repo.RegisterComponent<BrainBlackboard>();
        _registry.RegisterInstance(BpId, MakeDefinition());
    }

    public void Dispose() => _repo.Dispose();

    private byte* TierMemory(Entity e)
    {
        ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(e);
        return (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
    }

    private ParamsShape ReadParams(Entity e)
    {
        byte* mem = TierMemory(e);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, BpId, out int payloadOffset));
        return Unsafe.Read<ParamsShape>(mem + payloadOffset + CursorSize);
    }

    // ── the supply path ──────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The JSON reaches the params region.</b> This is the whole point of the seam: before it,
    /// an Instance had no way to be handed a value at all.
    /// </summary>
    [Fact]
    public void AttachWithJson_WritesTheResolvedParams()
    {
        var e = _repo.CreateEntity();
        var r = BlueprintInstanceService.AttachToEntity(
            _repo, _registry, BpId, e, "{\"Speed\":42,\"Range\":9.5}");

        Assert.Equal(BlueprintAttachStatus.Attached, r.Status);
        var p = ReadParams(e);
        Assert.Equal(42, p.Speed);
        Assert.Equal(9.5f, p.Range);
    }

    /// <summary>
    /// ⭐ <b>Absent key ⇒ the baked default stands</b> — that is what "overlay" means, and it is the
    /// same rule the generated <c>ParseParams</c> follows one level down.
    /// </summary>
    [Fact]
    public void AnOverlayOfOneParam_LeavesTheOtherAtItsDefault()
    {
        var e = _repo.CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, BpId, e, "{\"Speed\":3}");

        var p = ReadParams(e);
        Assert.Equal(3, p.Speed);
        Assert.Equal(DefaultRange, p.Range);
    }

    /// <summary>
    /// ⭐ <b>No JSON ⇒ declared defaults</b>, and every existing caller passes no JSON. ⚠ The editor's
    /// commit plan and the <c>[SharedAiAction]</c> attach node are both in this case: neither can carry
    /// a string, and "a caller with nothing to pass passes nothing" is the designed answer, not a gap.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AttachWithoutJson_LeavesTheDeclaredDefaults(string? json)
    {
        var e = _repo.CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, BpId, e, json);

        var p = ReadParams(e);
        Assert.Equal(DefaultSpeed, p.Speed);
        Assert.Equal(DefaultRange, p.Range);
    }

    // ── §8 rail: the cursor is not overwritten ───────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>§8's <c>startOffset: 0</c> rail.</b> <c>FieldLayout</c> used to lay parameters from
    /// offset <b>0</b> for BOTH dispatch kinds — and for an Instance, offset 0 IS the
    /// <see cref="BlueprintLatentCursor"/>. Resolving params would have shredded the latent scheduler's
    /// cursor and read as a scheduler bug, not a layout one.
    ///
    /// <para>
    /// ⚠ <b>This drives the REAL attach path, not <c>ParseParams</c> by hand.</b> A first draft called
    /// the delegate directly at <c>def.ParamsOffset</c> — and a revert probe showed that rail does not
    /// bite: it reads the offset from the very field under test, so reverting the layout left it green.
    /// The fixture's <c>InitDefault</c> stamps the cursor pattern instead, which only the attach path
    /// can then overwrite.
    /// </para>
    /// </summary>
    [Fact]
    public void ResolvingParams_DoesNotTouchTheLatentCursorAtOffsetZero()
    {
        var e = _repo.CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, BpId, e, "{\"Speed\":123,\"Range\":8}");

        byte* mem = TierMemory(e);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, BpId, out int payloadOffset));

        for (int i = 0; i < CursorSize; i++)
            Assert.Equal((byte)(0xA0 + i), mem[payloadOffset + i]);
        Assert.Equal(123, ReadParams(e).Speed);
        Assert.Equal(8f, ReadParams(e).Range);
    }

    /// <summary>
    /// ⭐⭐ <b>The ORDER is the ruling</b> (§3.3): <c>InitDefault</c> FIRST, then the resolved params.
    /// ⛔ The reverse zeroes them — and, because <c>InitDefault</c> legitimately clears the whole
    /// payload, it would look like the resolver never ran.
    /// </summary>
    [Fact]
    public void InitDefaultRunsBeforeTheParamsAreCopiedIn()
    {
        var e = _repo.CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, BpId, e, "{\"Speed\":55,\"Range\":2.5}");

        var p = ReadParams(e);
        Assert.Equal(55, p.Speed);       // ⛔ 0 here ⇒ InitDefault ran LAST and wiped the params.
        Assert.Equal(2.5f, p.Range);
    }

    // ── §8 rail: parse-before-commit ─────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Parse-before-commit</b>, mirroring <c>BehaviorIngressSystem</c>'s
    /// <i>"a failed parse leaves the entity 100% on its old behaviour"</i>.
    ///
    /// <para>
    /// ⛔ <b>NOT an allocated-then-freed slot</b> — the assertion is that no slot was EVER allocated.
    /// A free-after-allocate would already have touched the slot table and could have upgraded the
    /// tier, so "we rolled it back" is not the same guarantee.
    /// </para>
    /// </summary>
    [Fact]
    public void AMalformedPayload_AttachesNothingAtAll()
    {
        var e = _repo.CreateEntity();
        var r = BlueprintInstanceService.AttachToEntity(_repo, _registry, BpId, e, "{not json");

        Assert.Equal(BlueprintAttachStatus.ParamsParseFailed, r.Status);
        Assert.False(r.Success);
        // ⛔ No tier component was even added: the parse happens before EnsureTierComponent.
        Assert.False(_repo.HasComponent<BlueprintBlackboard1024>(e));
    }

    /// <summary>
    /// ⭐ And a failed parse on a SECOND attach leaves the FIRST blueprint's slot intact — the "old
    /// behaviour survives" half of the same guarantee.
    /// </summary>
    [Fact]
    public void AMalformedPayload_LeavesAnAlreadyAttachedInstanceUntouched()
    {
        const int otherId = unchecked((int)0xBEEF0002);
        _registry.RegisterInstance(otherId, MakeDefinition());

        var e = _repo.CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, BpId, e, "{\"Speed\":11}");

        var r = BlueprintInstanceService.AttachToEntity(_repo, _registry, otherId, e, "[]not-an-object{");
        Assert.Equal(BlueprintAttachStatus.ParamsParseFailed, r.Status);

        byte* mem = TierMemory(e);
        Assert.False(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, otherId, out _));
        Assert.Equal(11, ReadParams(e).Speed);
    }

    // ── §8 rail: the tail is untouched ───────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>§8's "the tail is untouched".</b> Resolving an Instance's params must not reach into the
    /// entity's <see cref="BrainBlackboard"/> — <c>ExpectedThreatLevel</c> and the two interrupts are
    /// entity FACTS, unrelated to params, and they live in the region the behaviour path's own
    /// <c>ParseParams</c> writes into. ⚠ One delegate type serving two destinations is exactly the
    /// shape where a wrong pointer would land there.
    /// </summary>
    [Fact]
    public void ResolvingInstanceParams_DoesNotWriteTheBrainBlackboardTail()
    {
        var e = _repo.CreateEntity();
        _repo.AddComponent(e, default(BrainBlackboard));
        ref var brain = ref _repo.GetComponentRW<BrainBlackboard>(e);
        brain.ExpectedThreatLevel     = 3;
        brain.Interrupt_MobilityLost  = 1;
        brain.Interrupt_Reserved      = 2;

        BlueprintInstanceService.AttachToEntity(_repo, _registry, BpId, e, "{\"Speed\":99,\"Range\":4}");

        ref var after = ref _repo.GetComponentRW<BrainBlackboard>(e);
        Assert.Equal(3, after.ExpectedThreatLevel);
        Assert.Equal(1, after.Interrupt_MobilityLost);
        Assert.Equal(2, after.Interrupt_Reserved);
        Assert.Equal(99, ReadParams(e).Speed);
    }

    /// <summary>
    /// ⭐ And nothing outside the slot's own payload moves either — the params copy is bounded by
    /// <c>ParamsSize</c> at <c>ParamsOffset</c>, so a neighbouring slot cannot be clipped.
    /// </summary>
    [Fact]
    public void ResolvingParams_WritesNothingOutsideItsOwnSlotPayload()
    {
        const int neighbourId = unchecked((int)0xBEEF0003);
        _registry.RegisterInstance(neighbourId, MakeDefinition());

        var e = _repo.CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, neighbourId, e, "{\"Speed\":8,\"Range\":1}");

        byte* mem = TierMemory(e);
        var before = new byte[BlueprintBlackboard1024.TotalSize];
        new Span<byte>(mem, before.Length).CopyTo(before);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, neighbourId, out int nbOffset));

        BlueprintInstanceService.AttachToEntity(_repo, _registry, BpId, e, "{\"Speed\":77,\"Range\":6}");

        mem = TierMemory(e);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, neighbourId, out int nbOffsetAfter));
        Assert.Equal(nbOffset, nbOffsetAfter);
        for (int i = 0; i < PayloadSize; i++)
            Assert.Equal(before[nbOffset + i], mem[nbOffset + i]);
    }

    // ── §8 rail: ONE supply mechanism ────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>§8's "one supply mechanism".</b> ⛔ <b><c>BlueprintAssignmentDto.Overrides</c> must stay
    /// unread</b> — the design supersedes it explicitly: <i>"Instances use the resolver, not a
    /// name→value dict."</i> A second, <c>Overrides</c>-style applier is the duplication ruling 9
    /// forbids, and it would supply the same bytes by a different route.
    ///
    /// <para>
    /// ⭐ The rail asks the ASSEMBLIES, not a grep of one file: no production type outside the DTO's own
    /// serialization may read the member.
    /// </para>
    /// </summary>
    [Fact]
    public void ExactlyOneParameterSupplyPathExists()
    {
        // 1. The definition exposes exactly one parameter-resolution entry point.
        var supplyMembers = typeof(BlueprintDefinition)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(Delegate).IsAssignableFrom(p.PropertyType)
                        && p.Name.Contains("Param", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToList();
        Assert.Equal(new[] { nameof(BlueprintDefinition.ParseParams) }, supplyMembers);

        // 2. It is the SAME delegate type the behaviour path uses — not a blueprint-only twin.
        Assert.Equal(
            typeof(Fdp.Toolkit.Behavior.ParseParamsDelegate),
            typeof(BlueprintDefinition).GetProperty(nameof(BlueprintDefinition.ParseParams))!.PropertyType);

        // 3. The attach seam takes the JSON and nothing else -- no dictionary, no side table.
        var attach = typeof(BlueprintInstanceService)
            .GetMethod(nameof(BlueprintInstanceService.AttachToEntity))!;
        var jsonParam = attach.GetParameters().Single(p => p.ParameterType == typeof(string));
        Assert.Equal("paramsJson", jsonParam.Name);
        Assert.DoesNotContain(attach.GetParameters(),
            p => typeof(System.Collections.IDictionary).IsAssignableFrom(p.ParameterType));
    }

    /// <summary>
    /// ⭐ A blueprint that declares no parameters keeps the shipped path exactly: no
    /// <c>ParseParams</c>, no scratch buffer, no copy. ⚠ This is 296 of 296 shipped Instance assets,
    /// so it is the case that must not move.
    /// </summary>
    [Fact]
    public void ABlueprintWithNoParameters_AttachesWithNoParamsWorkAtAll()
    {
        const int plainId = unchecked((int)0xBEEF0004);
        _registry.RegisterInstance(plainId, MakeDefinition(withParams: false));

        var e = _repo.CreateEntity();
        // ⭐ Even a malformed payload is irrelevant: with no ParseParams there is nothing to parse.
        var r = BlueprintInstanceService.AttachToEntity(_repo, _registry, plainId, e, "{not json");

        Assert.Equal(BlueprintAttachStatus.Attached, r.Status);
        byte* mem = TierMemory(e);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, plainId, out int off));
        // ⭐ Exactly what InitDefault left, and nothing else: no params copy ran over it.
        for (int i = 0; i < CursorSize; i++)  Assert.Equal((byte)(0xA0 + i), mem[off + i]);
        for (int i = CursorSize; i < PayloadSize; i++) Assert.Equal(0, mem[off + i]);
    }
}
