using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
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
}
