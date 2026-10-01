using System;
using System.Linq;
using Fdp.Toolkit.Behavior.Analyzers;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Golden;

/// <summary>
/// ⭐⭐⭐ <b><c>E7b</c> — <c>ExpressionTargetField</c> reaches emitted output.</b>
///
/// <para>
/// 🔴🔴 <b>Measured before this batch: ZERO occurrences in <c>HsmEmitCore</c> AND
/// <c>HsmBridgeEmitCore</c>.</b> The field round-tripped (<c>HsmAssetMapper:114/135</c>), the command
/// sink maintained it (<c>HsmCommandSink:249</c>), <c>HsmValidator:394</c> treated it as a
/// <b>writer style</b> for rule 9, and Batch 71 taught <c>CountNodesReferencingVariable</c> to count
/// it — ⛔ <b>and it never reached the blob, so there were no bytes to assert.</b> A producer with no
/// consumer.
/// </para>
///
/// <para>
/// ⭐⭐ <b>The mechanism already existed and was unreachable.</b> <c>HsmActionGenerator</c> emits a
/// per-binding thunk for every <c>[SharedAiAction]</c> — projecting the bound field at its byte
/// offset out of <c>bb.BehaviorParameters[0]</c> and calling the method — and registers it under a
/// COMPOUND key. Nothing on the asset side ever produced a compound key, so those registrations were
/// addressable by nobody.
/// </para>
///
/// <para>
/// ⛔⛔ <b>And the two spellings disagreed.</b> 📐 The sibling <c>BTreeActionGenerator</c> builds the
/// identical key as <c>ContainingType + "." + Name + "@" + offset</c> at all three of its sites;
/// <c>HsmActionGenerator</c> used the bare <c>sym.Name</c> at all three of its own. ⇒ <c>E6</c>(A)'s
/// ruling — the FQN is the identity — had not reached the compound key. ⭐ <b>Same defect class as
/// <c>E6</c>, one layer down</b>, and invisible for the same reason: a <c>TryGetValue</c> miss is
/// silent.
/// </para>
/// </summary>
public sealed class HsmExpressionTargetTests
{
    private const string BoundActionFqn = "Fdp.Toolkit.Behavior.Demo.DemoSharedActions.AlertNearbyUnits";

    /// <summary>
    /// ⭐ The REAL corpus asset, with its expression-target binding rewritten per case. ⛔ Not a
    /// hand-built DTO: an HSM asset's shape (root state, regions, event ids) is load-bearing for the
    /// emitter, and a synthetic stand-in that silently emits no transitions would make every
    /// assertion here vacuous — the failure mode this programme has caught four times.
    /// </summary>
    private static HsmAssetDto MakeBoundAsset(string? targetField)
    {
        var dto = HsmJsonServices.Deserialize(AiAssetCorpus.ReadAsset(AiAssetKind.Hsm, "HsmVariableShowcase"))!;
        var bound = dto.Transitions.Single(t => t.Action?.MethodFqn == BoundActionFqn);
        bound.Action!.ExpressionTargetField = targetField;   // CE-417: the ACTION binding's own field
        return dto;
    }

    // ══ the emitted key ══════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A bound transition is addressed by <c>{ActionFqn}@{offset}</c>, and the offset is the
    /// SHARED PACKER's.</b>
    ///
    /// <para>
    /// ⚠ The expected offset is taken from <c>BTreeBlackboardPackHelper</c> rather than written as a
    /// literal <c>4</c>: a literal would be a SECOND statement of the layout rule, and the whole point
    /// of routing through one packer is that <c>ParseParams</c> and the expression target cannot land
    /// on different bytes.
    /// </para>
    /// </summary>
    [Fact]
    public void ABoundTransition_BakesTheCompoundKey_AtThePackersOffset()
    {
        var dto  = MakeBoundAsset("Threshold");
        string core = HsmEmitCore.EmitTopologyCore(dto);

        int expected = PackedOffsetOf(dto, "Threshold");
        Assert.Contains($".Action(\"{BoundActionFqn}@{expected}\")", core);

        // ⭐ The builder registers the SAME string it later addresses. ⛔ If these diverged the blob
        //   would hash one name and the registration another — E6's silent TryGetValue miss.
        Assert.Contains($"builder.RegisterAction(\"{BoundActionFqn}@{expected}\");", core);
    }

    /// <summary>⭐ An UNBOUND transition is untouched — the bare FQN, exactly as before. This is what
    /// makes the change additive for every shipped asset that binds nothing.</summary>
    [Fact]
    public void AnUnboundTransition_KeepsTheBareFqn()
    {
        string core = HsmEmitCore.EmitTopologyCore(MakeBoundAsset(targetField: null));

        Assert.Contains($".Action(\"{BoundActionFqn}\")", core);
        Assert.DoesNotContain("@", CoreActionLines(core));
    }

    /// <summary>
    /// ⚠ <b>A target naming a variable that is not packed falls back to the bare FQN</b> rather than
    /// baking an offset that does not exist. <c>Role = State</c> variables live in the partition tier,
    /// so they are not inline params and cannot be an inline expression target.
    /// </summary>
    [Fact]
    public void ATargetThatIsNotAPackedParam_FallsBackToTheBareFqn()
    {
        var dto = MakeBoundAsset("Threshold");
        // "Cursor" is Role=State: it lives in the partition tier, so it is not an inline param.
        var dto2 = dto; dto2.Transitions.Single(t => t.Action?.MethodFqn == BoundActionFqn)
            .Action!.ExpressionTargetField = "Cursor";

        string core = HsmEmitCore.EmitTopologyCore(dto);

        Assert.Contains($".Action(\"{BoundActionFqn}\")", core);
    }

    // ══ the two sides of the netstandard2.0 wall ════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>End to end across the two generators: the id the ASSET emits is the id the REGISTRAR
    /// registers.</b>
    ///
    /// <para>
    /// ⚠ Both sides are produced, not derived: the left side is <c>HsmEmitCore</c>'s emitted string
    /// run through <c>HsmActionKey</c>; the right side is read out of the text
    /// <c>HsmActionGenerator</c> really generated for a <c>[SharedAiAction]</c> method at the same
    /// offset. ⛔ Computing both from the same rule would make this agree with itself — the mistake
    /// Batch 72 caught in an <c>E6</c> rail of mine.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>RED before this batch:</b> the registrar keyed the compound on the SIMPLE name, so the
    /// two ids differed and no asset could ever address a bound thunk.
    /// </para>
    /// </summary>
    [Fact]
    public void TheAssetsIdIsTheRegistrarsId_ForABoundAction()
    {
        // ⭐⭐⭐ CE-417 B-2 (a′) — the registrar is now the ASSET's own (HsmBridgeEmitCore), not HsmActionGenerator's per-method
        //   table. Both halves come out of emitting the asset with the compilation's answer (here a stand-in for
        //   "AlertNearbyUnits is a [SharedAiAction] taking ref float"), and they must agree on the id.
        var dto = MakeBoundAsset("Threshold");
        int offset = PackedOffsetOf(dto, "Threshold");
        System.Func<string, Hrot.AiEditor.Persistence.Emit.SharedAiMethodInfo?> sharedAi = fqn =>
            fqn == BoundActionFqn ? new Hrot.AiEditor.Persistence.Emit.SharedAiMethodInfo("float", "System.Single", false, false) : null;

        string topology = HsmEmitCore.EmitTopologyCore(dto, null, null, null, null, sharedAi);
        string argument = ActionArgumentIn(topology);
        Assert.Equal(BoundActionFqn + "@" + offset, argument);

        ushort fromAsset = Fnv1a16(argument);
        string registrar = HsmBridgeEmitCore.EmitBridge(dto, null, sharedAi);
        Assert.Contains($"RegisterAction({fromAsset},", registrar);
        Assert.Contains($"(__root + {offset})", registrar);
        Assert.DoesNotContain("SeedParamsOffset", registrar);   // F4: no state base
    }

    // ══ helpers ══════════════════════════════════════════════════════════════

    /// <summary>
    /// ⚠ Recomputed here rather than calling <c>HsmActionKey.ForCompoundKey</c>: that type is
    /// <c>internal</c> to the analyzer, deliberately. ⭐ The independence is the point — the sibling
    /// <c>HsmActionIdAgreementTests</c> derives the flattener's answer the same way, so a drift on
    /// either side of the netstandard2.0 wall shows up as a disagreement rather than as two callers
    /// of one helper agreeing with themselves.
    /// </summary>
    private static ushort Fnv1a16(string s)
    {
        uint hash = 2166136261;
        foreach (char c in s) { hash ^= c; hash *= 16777619; }
        return (ushort)(hash & 0xFFFF);
    }

    private static int PackedOffsetOf(HsmAssetDto dto, string variableName)
        => BTreeBlackboardPackHelper.Pack(
               dto.Blackboard.Variables.Select(v => new Hrot.AiEditor.Persistence.BTree.BlackboardVariableDto
               {
                   Name = v.Name,
                   Type = new Hrot.AiEditor.Persistence.BTree.BlackboardTypeRefDto { TypeId = v.Type.TypeId },
                   Role = v.Role,
               }).ToList(),
               out _)
           .Single(f => f.Name == variableName).ByteOffset;

    /// <summary>The single argument of the emitted <c>.Action("…")</c> call.</summary>
    private static string ActionArgumentIn(string core)
    {
        string line = CoreActionLines(core);
        int s = line.IndexOf(".Action(\"", StringComparison.Ordinal) + ".Action(\"".Length;
        int e = line.IndexOf('"', s);
        return line.Substring(s, e - s);
    }

    private static string CoreActionLines(string core)
        => string.Join("\n", core.Split('\n').Where(l => l.Contains(".Action(\"")));

}
