using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-281</c> — HSM's <c>ParseParams</c> counterpart.</b>
///
/// <para>
/// 🔴🔴 <b>Measured in Batch 71:</b> an HSM <c>Role = Input</c> variable reached <b>no</b> emitted
/// output — not the topology core, not the registrar; <c>HsmBridgeEmitCore</c> emitted a slot
/// manifest and <b>no params handling of any kind</b>. ⇒ ⛔ you could author an input, round-trip it,
/// see it in the editor's own section, and <b>nothing wrote it at runtime</b>. 📄 The plan's §4B
/// thesis in its purest form: <i>HSM's authoring model is ahead of its runtime.</i>
/// </para>
///
/// <para>
/// ⭐⭐⭐ <b>The rails that matter here assert BYTES, not emitted text.</b> Batch 73's finding —
/// <i>a gate that cannot name its cause is not a gate</i> — generalises to this: a rail that only
/// greps the emitted string proves the emitter said something, never that the something WORKS.
/// So <see cref="CompileEmittedParseParams"/> takes the registrar the emitter really produced,
/// compiles it, and <b>runs it over real memory</b>. ⛔ The text rails below exist only for the
/// cases where the claim IS about the text (a guard emitting nothing at all).
/// </para>
/// </summary>
public sealed class HsmParseParamsEmissionTests
{
    private static readonly Guid AssetId = new("bb281000-0000-0000-0000-000000000281");

    private sealed record Var(string Name, string TypeId, string? DefaultJson,
                              BlackboardVariableRole Role = BlackboardVariableRole.Input,
                              WorkingStateScope Scope = WorkingStateScope.Node);

    private static HsmAssetDto MakeDto(bool managed, params Var[] vars)
    {
        var dto = new HsmAssetDto { AssetId = AssetId, Name = "ParamsHsm" };
        dto.Blackboard.Managed = managed;
        foreach (var v in vars)
        {
            dto.Blackboard.Variables.Add(new HsmBlackboardVariableDto
            {
                Name             = v.Name,
                Type             = new HsmBlackboardTypeRefDto { TypeId = v.TypeId },
                DefaultValueJson = v.DefaultJson,
                Role             = v.Role,
                Scope            = v.Scope,
            });
        }
        return dto;
    }

    /// <summary>Two floats and an int: offsets 0, 4 and 8 under natural alignment.</summary>
    private static HsmAssetDto ThreeVarDto() => MakeDto(true,
        new Var("Threshold", "System.Single", "1.5"),
        new Var("Speed",     "System.Single", "2.5"),
        new Var("Count",     "System.Int32",  null));

    // ══ the BYTES rails ══════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>Rail 1 — an authored default reaches the params region at activation.</b>
    /// 🔴 Before <c>BP-281</c> there was nothing to reach it with: the HSM bridge emitted no
    /// <c>ParseParams</c>, so <c>BehaviorIngressSystem</c> skipped the parse step entirely and the
    /// region kept whatever the previous behaviour left there.
    /// </summary>
    [Fact]
    public unsafe void AnAuthoredDefault_IsWrittenIntoTheParamsRegion()
    {
        var parse = CompileEmittedParseParams(ThreeVarDto());

        byte* memory = stackalloc byte[64];
        for (int i = 0; i < 64; i++) memory[i] = 0xEE;   // poison: a no-op parse would be visible

        parse(string.Empty, memory, null!, default, null);

        (*(float*)(memory + 0)).Should().Be(1.5f, "Threshold's authored default");
        (*(float*)(memory + 4)).Should().Be(2.5f, "Speed's authored default");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail 2 — an incoming JSON overlay WINS over the default, per variable, and leaves the
    /// others alone.</b> 📌 This is <c>DEBT-AIB-021</c>'s decision reproduced on the second host: the
    /// defaults are baked FIRST and the overlay runs SECOND, so the order is what makes both true at
    /// once (📄 <c>DESIGN_Parameter_Model.md</c> §3.2 — <i>the ORDER is the ruling</i>).
    /// </summary>
    [Fact]
    public unsafe void AnIncomingOverlay_WinsPerVariable_AndLeavesTheRestOnTheirDefaults()
    {
        var parse = CompileEmittedParseParams(ThreeVarDto());

        byte* memory = stackalloc byte[64];
        parse("{\"Threshold\":9.5}", memory, null!, default, null);

        (*(float*)(memory + 0)).Should().Be(9.5f, "the overlay wins for the variable it names");
        (*(float*)(memory + 4)).Should().Be(2.5f, "a variable the overlay does not name keeps its default");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail 3 — <c>DEBT-AIB-021</c>'s DEFECT (b), on this host: an asset whose inputs have NO
    /// defaults still gets a WORKING <c>ParseParams</c>.</b>
    ///
    /// <para>
    /// ⛔ The BTree bridge's original guard was <i>"≥1 default"</i>, which meant an asset with
    /// variables but no defaults emitted nothing and could never be overridden at all. ⭐ Copying that
    /// guard would have reproduced the defect on a second host — so the condition here is <b>"≥1
    /// PACKED variable"</b>, and this rail is what holds it there.
    /// </para>
    /// </summary>
    [Fact]
    public unsafe void AnAssetWithNoDefaultsAtAll_StillGetsAWorkingParseParams()
    {
        var parse = CompileEmittedParseParams(MakeDto(true,
            new Var("Count", "System.Int32", null),
            new Var("Limit", "System.Int32", null)));

        byte* memory = stackalloc byte[64];
        parse("{\"Limit\":7}", memory, null!, default, null);

        (*(int*)(memory + 4)).Should().Be(7, "the overlay is useful with or without defaults");
    }

    /// <summary>
    /// ⭐ <b>An unknown key is IGNORED, not an error</b> — matching the curated path's own behaviour
    /// and the BTree bridge's decision test. ⚠ Asserted by BYTES: the named variables still land.
    /// </summary>
    [Fact]
    public unsafe void AnUnknownKey_IsIgnored()
    {
        var parse = CompileEmittedParseParams(ThreeVarDto());

        byte* memory = stackalloc byte[64];
        parse("{\"NoSuchVariable\":123,\"Threshold\":4.25}", memory, null!, default, null);

        (*(float*)(memory + 0)).Should().Be(4.25f);
    }

    /// <summary>
    /// ⛔⛔ <b>Malformed JSON THROWS, deliberately.</b> <c>BehaviorIngressSystem</c> parses into a
    /// stack shadow and commits only on success (<c>:92-121</c>), so a throw is exactly what leaves
    /// the entity on its old behaviour. ⚠ Swallowing would look tidier and hand it a
    /// successful-looking, half-written params region.
    /// </summary>
    [Fact]
    public unsafe void MalformedJson_IsNotSwallowed()
    {
        var parse = CompileEmittedParseParams(ThreeVarDto());

        byte* memory = stackalloc byte[64];
        byte* captured = memory;
        Action act = () => parse("{ this is not json", captured, null!, default, null);

        act.Should().Throw<Exception>();
    }

    // ══ the GUARD rails — here the claim really is about the text ════════════

    /// <summary>
    /// ⭐⭐⭐ <b>ONE guard, not three.</b> A managed blackboard whose variables are ALL
    /// <c>Role = State</c> packs to no inline fields — state lives in the partition tier — so it must
    /// emit <b>none</b> of the three pieces: the pragma, the options field, the delegate.
    ///
    /// <para>
    /// 📌 <b>This is the rail that would have caught <c>DEBT-AIB-021</c>'s defects (b) and (c).</b>
    /// Those were not "the wrong condition" — they were TWO conditions that disagreed with each
    /// other. ⇒ the rail asserts all three emissions move together, which no single-piece test can.
    /// </para>
    /// </summary>
    [Fact]
    public void AManagedAssetOfOnlyStateVariables_EmitsNoneOfTheThreePieces()
    {
        string bridge = HsmBridgeEmitCore.EmitBridge(MakeDto(true,
            new Var("Cursor", "System.Int32", null, BlackboardVariableRole.State, WorkingStateScope.Behavior)));

        bridge.Should().NotContain("#nullable enable");
        bridge.Should().NotContain("__paramJsonOpts");
        bridge.Should().NotContain("__parseParams");
        bridge.Should().Contain("StatefulWorkingSlots", "the manifest is E1's job and is unaffected");
    }

    /// <summary>
    /// ⭐⭐ <b><c>DEBT-AIB-021</c>'s DEFECT (c), one level up:</b> the <c>JsonSerializerOptions</c>
    /// field's guard must NOT be keyed on <i>"≥1 default"</i> either — the overlay deserializes
    /// through it whether or not anything was defaulted.
    /// </summary>
    [Fact]
    public void TheJsonOptionsField_IsEmittedForAnAssetWithVariablesButNoDefaults()
    {
        string bridge = HsmBridgeEmitCore.EmitBridge(MakeDto(true,
            new Var("Count", "System.Int32", null)));

        // ⚠⚠ The DECLARATION, not the name. 📌 Written first as `Contain("__paramJsonOpts")` and
        //    caught by this item's own revert probe: the emitted BODY calls Deserialize(…,
        //    __paramJsonOpts), so the bare name is present even when the field is gone — the rail was
        //    satisfied by the very code that needs the field. ⭐ Fifth instance in this programme of
        //    "ask the artefact, not the thing that produced it."
        bridge.Should().Contain(
            "private static readonly global::System.Text.Json.JsonSerializerOptions __paramJsonOpts =");
        bridge.Should().Contain("ParseParams   = __parseParams,");
    }

    /// <summary>⭐ A non-managed blackboard reflects a hand-written struct — the editor owns no
    /// layout for it, so it emits no params supply and stays byte-identical.</summary>
    [Fact]
    public void ANonManagedAsset_EmitsNoParseParams()
    {
        string bridge = HsmBridgeEmitCore.EmitBridge(MakeDto(false,
            new Var("Threshold", "System.Single", "1.5")));

        bridge.Should().NotContain("__parseParams");
    }

    /// <summary>
    /// ⭐⭐ <b>The PACKER has one home</b> (ruling 9, and the choice <c>E1</c> already made for the
    /// slot key). ⚠ Asserted against <c>BTreeBlackboardPackHelper</c>'s own output rather than against
    /// literals: a hand-written <c>0, 4, 8</c> here would be a SECOND statement of the layout rule,
    /// which is precisely the thing being prevented.
    /// </summary>
    [Fact]
    public void TheOffsetsAreTheSharedPackersOwn_NotASecondLayoutRule()
    {
        string bridge = HsmBridgeEmitCore.EmitBridge(ThreeVarDto());

        var packed = BTreeBlackboardPackHelper.Pack(
            ThreeVarDto().Blackboard.Variables.Select(v => new Hrot.AiEditor.Persistence.BTree.BlackboardVariableDto
            {
                Name = v.Name,
                Type = new Hrot.AiEditor.Persistence.BTree.BlackboardTypeRefDto { TypeId = v.Type.TypeId },
                Role = v.Role,
            }).ToList(),
            out _);

        foreach (var f in packed)
            bridge.Should().Contain($"memory + {f.ByteOffset}",
                $"'{f.Name}' must be written at the shared packer's offset");
    }

    // ══ the harness ══════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Compiles the ParseParams the emitter REALLY produced and returns it as a live
    /// delegate.</b>
    ///
    /// <para>
    /// ⭐ The lambda is SLICED out of the emitted registrar rather than rebuilt here — between the
    /// declaration of <c>__parseParams</c> and the definition registration that consumes it. ⛔ Any
    /// re-statement of the emitted shape in this file would make the rail agree with itself: the
    /// mistake this programme has caught four times, most recently in Batch 72.
    /// </para>
    ///
    /// <para>
    /// ⚠ Only the registrar's <b>surroundings</b> are supplied here (the options field, a host
    /// class), because the full registrar also needs the topology core, <c>BehaviorRegistry</c> and
    /// the Fhsm kernel — none of which the params supply touches.
    /// </para>
    /// </summary>
    private static Fdp.Toolkit.Behavior.ParseParamsDelegate CompileEmittedParseParams(HsmAssetDto dto)
    {
        string bridge = HsmBridgeEmitCore.EmitBridge(dto);

        // ⭐⭐ BOTH halves come from the emitter: the options FIELD and the ParseParams LOCAL.
        // ⛔ Supplying a hand-written options field here would hide DEBT-AIB-021's defect (c)
        //    exactly — a body that deserializes through a field the emitter forgot to declare
        //    compiles fine against a stand-in and not at all in production.
        string fieldSlice = Slice(bridge,
            "private static readonly global::System.Text.Json.JsonSerializerOptions __paramJsonOpts =",
            "/// <summary>");
        string localSlice = Slice(bridge,
            "global::Fdp.Toolkit.Behavior.ParseParamsDelegate? __parseParams;",
            "// Register the JSON-owned HSM definition.");

        localSlice.Should().NotBeEmpty("the emitter must declare a ParseParams local for this asset");

        string host = @"#nullable enable
public static unsafe class __ParseParamsHost
{
" + fieldSlice + @"
    public static global::Fdp.Toolkit.Behavior.ParseParamsDelegate Make()
    {
" + localSlice + @"
        return __parseParams!;
    }
}";

        var compilation = CSharpCompilation.Create(
            "ParseParamsProbe_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(host) },
            ReferenceSet(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        using var peStream = new MemoryStream();
        var result = compilation.Emit(peStream);
        result.Success.Should().BeTrue(
            "the emitted ParseParams must COMPILE: "
            + string.Join(Environment.NewLine,
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        peStream.Position = 0;
        var asm = new AssemblyLoadContext("ParseParamsProbe", isCollectible: false)
            .LoadFromStream(peStream);

        var make = asm.GetType("__ParseParamsHost")!.GetMethod("Make", BindingFlags.Public | BindingFlags.Static)!;
        return (Fdp.Toolkit.Behavior.ParseParamsDelegate)make.Invoke(null, null)!;
    }

    /// <summary>Text between <paramref name="start"/> (inclusive) and <paramref name="end"/>
    /// (exclusive); empty when either marker is absent.</summary>
    private static string Slice(string text, string start, string end)
    {
        int s = text.IndexOf(start, StringComparison.Ordinal);
        if (s < 0) return string.Empty;
        int e = text.IndexOf(end, s, StringComparison.Ordinal);
        return e < 0 ? string.Empty : text.Substring(s, e - s);
    }

    /// <summary>Every assembly already loaded in this test process, plus the framework facades.</summary>
    private static IReadOnlyList<MetadataReference> ReferenceSet()
    {
        var refs = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.IsDynamic || string.IsNullOrEmpty(a.Location)) continue;
            if (!seen.Add(a.Location)) continue;
            refs.Add(MetadataReference.CreateFromFile(a.Location));
        }

        // Force-load the two the probe binds against but the test host may not have touched yet.
        foreach (var t in new[] { typeof(Fdp.Toolkit.Behavior.ParseParamsDelegate), typeof(Fdp.Core.Entity) })
            if (seen.Add(t.Assembly.Location))
                refs.Add(MetadataReference.CreateFromFile(t.Assembly.Location));

        string dir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var name in new[] { "System.Runtime.dll", "System.Text.Json.dll", "netstandard.dll" })
        {
            string p = Path.Combine(dir, name);
            if (File.Exists(p) && seen.Add(p))
                refs.Add(MetadataReference.CreateFromFile(p));
        }
        return refs;
    }
}
