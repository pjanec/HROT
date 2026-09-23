using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Lowering;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-247</c> — a persisted default value becomes a C#-TYPED literal, or it is refused.</b>
///
/// <para>
/// 🔴 <b>Before:</b> <c>Stage5:107</c> and <c>:4681</c> assigned <c>DefaultValueCSharp</c> from
/// <c>DefaultValueJson</c> <b>verbatim</b>, so a <c>float</c> default of <c>0.5</c> emitted
/// <c>s.Ratio = 0.5;</c> — a <c>double</c> literal — and <b>Roslyn</b> refused it with <c>CS0664</c>
/// naming a generated file. ⛔ A diagnostic in the wrong language, the <c>__var_-1</c> / <c>BP-228</c>
/// shape again.
/// </para>
///
/// <para>
/// ⚠ <b>The corpus cannot witness this</b> — measured: every shipped default is integral, <c>false</c>,
/// or absent. Every fixture here constructs the asset the corpus does not contain.
/// </para>
/// </summary>
public sealed class DefaultLiteralTests
{
    private static CompileResult CompileVariable(string typeId, string? defaultJson)
    {
        var asset = BlueprintAssetBuilder
            .Instance("DefaultLiteralFixture")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        asset.Variables.Add(new VariableDecl
        {
            Id               = Guid.NewGuid(),
            Name             = "Value",
            Type             = new BlueprintTypeRef { TypeId = typeId },
            DefaultValueJson = defaultJson,
        });
        return new Hrot.Blueprints.Core.Compiler.BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
    }

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 the finding itself
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>RED before:</b> emitted <c>s.Value = 0.5;</c> and the in-memory Roslyn compile failed
    /// <c>CS0664</c>. ⭐ Green means the emitted literal carries the <c>F</c> suffix that types it.
    /// </summary>
    [Fact]
    public void AFractionalFloatDefault_EmitsAFloatLiteral()
    {
        var result = CompileVariable("float", "0.5");

        Assert.True(result.Succeeded, Diags(result));
        Assert.Contains("s.Value = 0.5F;", result.GeneratedSource);
        Assert.DoesNotContain("s.Value = 0.5;", result.GeneratedSource);
    }

    /// <summary>
    /// 🔴🔴 <b>The defect as it was actually met — through REAL Roslyn.</b> ⚠ The test above compares the
    /// emitted TEXT, which is a proxy; this one hands the generated source to the C# compiler, which is
    /// what produced <c>CS0664</c> in the first place. ⛔ Without it the fixture would be asserting that
    /// the emitter writes what the emitter writes.
    /// </summary>
    [Fact]
    public void AFractionalFloatDefault_SurvivesTheRealCSharpCompiler()
    {
        var asset = BlueprintAssetBuilder
            .Instance("DefaultLiteralRoslynFixture")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        asset.Variables.Add(new VariableDecl
        {
            Id               = Guid.NewGuid(),
            Name             = "Ratio",
            Type             = new BlueprintTypeRef { TypeId = "float" },
            DefaultValueJson = "0.5",
        });

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // 🔴 RED before: `s.Ratio = 0.5;` — a double literal — and this threw with CS0664 naming
        //    `Bp_<guid>.g.cs`, a file the designer has never seen.
        fixture.CompileAndLoad(asset, GoldenCorpus.Options());
    }

    /// <summary>
    /// ⭐ <b>The whole family, not just the one the fixture tripped over</b> — every numeric type whose
    /// bare literal C# would type as something else.
    /// </summary>
    [Theory]
    [InlineData("float",  "1.25",  "1.25F")]
    [InlineData("float",  "3",     "3F")]
    [InlineData("double", "1.25",  "1.25D")]
    [InlineData("long",   "-9000", "-9000L")]
    [InlineData("ulong",  "9000",  "9000UL")]
    [InlineData("uint",   "7",     "7U")]
    [InlineData("int",    "-1",    "-1")]
    [InlineData("short",  "-1",    "-1")]
    [InlineData("bool",   "true",  "true")]
    public void EveryNumericDefault_IsTypedByItsDeclaration(string typeId, string json, string expected)
    {
        var result = CompileVariable(typeId, json);

        Assert.True(result.Succeeded, Diags(result));
        Assert.Contains($"s.Value = {expected};", result.GeneratedSource);
    }

    // ────────────────────────────────────────────────────────────────────────
    // ⭐⭐ refusal, not pass-through
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The half that matters more than the suffixes.</b> A literal the converter cannot type must
    /// produce <c>BP1674</c> against the DECLARATION — ⛔ not a pass-through that becomes a Roslyn error
    /// naming a file the designer has never seen.
    /// </summary>
    [Theory]
    [CoversDiagnosticCode("BP1674")]
    [InlineData("int",     "not-a-number")]
    [InlineData("float",   "1.0.0")]
    [InlineData("bool",    "True")]        // JSON is lower-case; `True` is not a C# literal either
    [InlineData("byte",    "300")]         // parses as a number, does not fit the declared type
    [InlineData("Vector3", "[0, 1, 0]")]   // no literal form at all
    public void AnUntypableDefault_IsRefusedByTheCompilerNotByRoslyn(string typeId, string json)
    {
        var result = CompileVariable(typeId, json);

        Assert.False(result.Succeeded, "the compile should have been refused, not handed to Roslyn.");
        Assert.Contains(result.Diagnostics, d => d.Code == "BP1674");
        // ⭐ The message names the declaration, in the compiler's own language.
        Assert.Contains(result.Diagnostics, d => d.Code == "BP1674" && d.Message.Contains("Value"));
    }

    /// <summary>
    /// ⭐ <b>An ABSENT default stays absent</b> — for a struct type there is nothing to write, and that
    /// is a success rather than the refusal above. ⚠ 12 shipped declarations rely on this.
    /// </summary>
    [Theory]
    [InlineData("Vector3", null)]
    [InlineData("Vector3", "")]
    [InlineData("Fdp.Core.Entity", null)]
    // ⚠⚠ A ZERO on a type with no literal form is not a refusal either, and that is the
    //    pre-existing contract rather than an indulgence: the emitters skipped `"0"` for EVERY type,
    //    so the editor's default `0` on a list or a struct has always meant "leave it zeroed".
    //    Refusing it broke the `ListVariable*` fixtures and the `ListVariableDemo` recipe — caught by
    //    the suite, not by reasoning.
    [InlineData("Vector3", "0")]
    [InlineData("Fdp.Core.Entity", "0")]
    public void AnAbsentDefault_IsNotAnError(string typeId, string? json)
    {
        var result = CompileVariable(typeId, json);

        Assert.True(result.Succeeded, Diags(result));
        Assert.DoesNotContain("s.Value =", result.GeneratedSource);
    }

    /// <summary>
    /// ⭐⭐ <b>A zero default emits nothing, for every type — the pre-existing contract, pinned.</b> The
    /// emitters tested <c>DefaultValueCSharp != "0"</c> unconditionally, so 45 shipped <c>float</c>
    /// fields, a <c>bool</c> output and several list variables all carry a <c>0</c> that has always meant
    /// "leave it zero-initialised". ⛔ Typing it instead would have emitted 45 assignments writing a zero
    /// over a zero, and refusing it would have failed assets that ship today.
    /// </summary>
    [Theory]
    [InlineData("float", "0")]
    [InlineData("int",   "0")]
    [InlineData("long",  "0")]
    [InlineData("bool",  "0")]
    public void AZeroDefault_EmitsNoAssignmentAtAll(string typeId, string json)
    {
        var result = CompileVariable(typeId, json);

        Assert.True(result.Succeeded, Diags(result));
        Assert.DoesNotContain("s.Value =", result.GeneratedSource);
    }

    private static string Diags(CompileResult r)
        => string.Join(", ", r.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    // ════════════════════════════════════════════════════════════════════════
    // ⭐⭐⭐ CE-300 — the LITERAL NODE path, the other half BP-247 never typed
    // ════════════════════════════════════════════════════════════════════════
    //
    // ⚠⚠ THE FILED FIX SHAPE WAS WRONG, AND THE CORPUS IS WHAT SAYS SO. CE-300 prescribed routing
    //   LiteralNode through TryToCSharp and REFUSING what it cannot type. 📐 Measured across the 104
    //   shipped assets: ValueJson does NOT hold JSON, it holds C# SOURCE TEXT — `0f`, `-1L`,
    //   `(ushort)0`, `"HullDownAttack"`, `global::…NavigationResult.Arrived`. TryToCSharp parses JSON,
    //   so it refuses 42 of the 86 literal nodes that ship. ⇒ convert-or-PASS-THROUGH, never
    //   convert-or-refuse. 📄 DefaultLiteral.ForLiteralNode.

    /// <summary>
    /// 🔴🔴 <b>The defect itself, through REAL Roslyn.</b> A hand-authored <c>System.Single</c> literal
    /// spelled <c>0.2777778</c> — no <c>f</c> — emitted <c>var __t2 = 0.2777778;</c> and Roslyn refused
    /// the GENERATED file with <c>CS0266: cannot implicitly convert 'double' to 'float'</c>.
    /// ⚠ The editor's drawer appends the suffix, so the guard was <b>a drawer, not a compiler rule</b>:
    /// any hand-authored asset, recipe or future tool reproduces it.
    /// </summary>
    [Fact]
    public void CE300_AnUntypedFloatLiteralNode_SurvivesTheRealCSharpCompiler()
    {
        var asset = BuildLiteralIntoVariable("System.Single", "0.2777778", typeof(float));

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // 🔴 RED before: CS0266 naming `Bp_<guid>.g.cs`, a file the designer has never seen.
        fixture.CompileAndLoad(asset, GoldenCorpus.Options());
    }

    /// <summary>
    /// ⭐⭐ <b>The emitted TEXT, so a green above cannot be luck.</b> ⛔ Roslyn would also accept
    /// <c>(float)0.2777778</c> or a rewritten constant; this pins that the fix is the SUFFIX.
    /// </summary>
    [Fact]
    public void CE300_AnUntypedFloatLiteralNode_EmitsAFloatSuffixedLiteral()
    {
        var result = new Hrot.Blueprints.Core.Compiler.BlueprintCompiler()
            .Compile(BuildLiteralIntoVariable("System.Single", "0.2777778", typeof(float)),
                     GoldenCorpus.Options());

        Assert.True(result.Succeeded, Diags(result));
        Assert.Contains("0.2777778F", result.GeneratedSource);
        Assert.DoesNotContain("= 0.2777778;", result.GeneratedSource);
    }

    /// <summary>
    /// ⛔⛔⛔ <b>THE RAIL THAT GUARDS THE CORPUS, and it is the one the filed fix would have broken.</b>
    ///
    /// <para>📐 Every value below is a REAL spelling taken from the 104 shipped assets. None of them is
    /// JSON, and <see cref="DefaultLiteral.TryToCSharp"/> refuses every one — so a convert-or-refuse
    /// fix would have turned a latent authoring trap into a corpus-wide build break.
    /// ⭐ <c>ForLiteralNode</c> must hand each one back UNCHANGED.</para>
    ///
    /// <para>⚠ <c>"0"</c> is the subtle one and it is deliberately included: <c>TryToCSharp</c> answers
    /// <c>true</c> with an EMPTY literal for a zero — its <i>"leave it zero-initialised"</i> contract
    /// for DECLARATION defaults. A literal NODE has no such contract; emitting nothing produces
    /// <c>var __t5 = ;</c>. ⇒ the empty-conversion guard is load-bearing, not defensive.</para>
    /// </summary>
    [Theory]
    [InlineData("System.Single", "0f")]                     // 15 assets spell it this way
    [InlineData("System.Single", "5f")]
    [InlineData("System.Single", "0.2777778f")]             // already suffixed — must not be re-suffixed
    [InlineData("System.Int64",  "-1L")]
    [InlineData("System.Int64",  "777L")]
    [InlineData("System.UInt16", "(ushort)0")]              // a C# CAST, not a number
    [InlineData("System.Byte",   "(byte)0")]
    [InlineData("System.String", "\"HullDownAttack\"")]     // quoted, and String is not in the switch
    [InlineData("global::Fdp.Toolkit.Navigation.NavigationResult",
                "global::Fdp.Toolkit.Navigation.NavigationResult.Arrived")]
    [InlineData("System.Int32",  "0")]                      // ⚠ the zero-shortcut trap
    [InlineData("System.Int32",  "7")]                      // converts to itself — still unchanged
    [InlineData("System.Boolean","true")]
    public void CE300_EveryCorpusLiteralSpelling_PassesThroughUnchanged(string typeId, string value)
    {
        var type = new IrTypeRef { FullName = typeId };

        Assert.Equal(value, DefaultLiteral.ForLiteralNode(type, value));
    }

    /// <summary>
    /// ⭐ The conversion half, stated as its own claim: a bare decimal on a float pin GAINS the suffix.
    /// ⛔ Paired with the theory above so neither half can be satisfied by doing nothing.
    /// </summary>
    [Theory]
    [InlineData("System.Single", "0.2777778", "0.2777778F")]
    [InlineData("System.Single", "1.5",       "1.5F")]
    [InlineData("System.Double", "1.5",       "1.5D")]
    [InlineData("System.Int64",  "777",       "777L")]
    [InlineData("System.UInt32", "7",         "7U")]
    public void CE300_AnUntypedNumericLiteral_GainsItsTypeSuffix(string typeId, string value, string expected)
    {
        var type = new IrTypeRef { FullName = typeId };

        Assert.Equal(expected, DefaultLiteral.ForLiteralNode(type, value));
    }

    /// <summary>
    /// An Instance asset whose Tick graph is <c>Entry → SetVariable(Value) ← Literal → Return</c>.
    /// ⭐ The builder has no literal-node affordance, so the graph is hand-built — the shape
    /// <c>Stage5VarPrefixResolutionTests</c> already uses.
    /// </summary>
    private static BlueprintAsset BuildLiteralIntoVariable(string literalTypeId, string valueText, Type clrType)
    {
        var asset = BlueprintAssetBuilder
            .Instance("Ce300LiteralFixture")
            .WithVariable("Value", clrType)
            .Build();

        var decl = Assert.Single(asset.Variables);

        Guid entryId = Guid.NewGuid(), setId = Guid.NewGuid(), litId = Guid.NewGuid(), retId = Guid.NewGuid();
        Guid entryOut = Guid.NewGuid(), setIn = Guid.NewGuid(), setOut = Guid.NewGuid(),
             setValue = Guid.NewGuid(), litOut = Guid.NewGuid(), retIn = Guid.NewGuid();

        asset.Graphs.Add(new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Tick",
            Kind  = GraphKind.Event,
            Nodes =
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = { new Pin { Id = entryOut, Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() } },
                },
                new SetVariableNode
                {
                    Id         = setId,
                    VariableId = decl.Id.ToString(),
                    Pins =
                    {
                        new Pin { Id = setIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = setOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = setValue, Name = "Value", Direction = "In",  IsExec = false,
                                  TypeRef = new BlueprintTypeRef { TypeId = literalTypeId } },
                    },
                },
                new LiteralNode
                {
                    Id        = litId,
                    TypeId    = literalTypeId,
                    ValueJson = valueText,
                    Pins =
                    {
                        new Pin { Id = litOut, Name = "Value", Direction = "Out", IsExec = false,
                                  TypeRef = new BlueprintTypeRef { TypeId = literalTypeId } },
                    },
                },
                new ReturnNode
                {
                    Id     = retId,
                    Status = NodeStatus.Success,
                    Pins   = { new Pin { Id = retIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } },
                },
            },
            Links =
            {
                new Link { FromNodeId = entryId, FromPinId = entryOut, ToNodeId = setId, ToPinId = setIn },
                new Link { FromNodeId = setId,   FromPinId = setOut,   ToNodeId = retId, ToPinId = retIn },
                new Link { FromNodeId = litId,   FromPinId = litOut,   ToNodeId = setId, ToPinId = setValue },
            },
        });

        return asset;
    }
}
