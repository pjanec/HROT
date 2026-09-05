using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-203 — <see cref="BlueprintTypeSystem.AreCompatible"/> used to compare raw <c>TypeId</c>
/// strings and hand-list exactly ONE coercion rung (<c>Int32 -&gt; Single</c>), while the compiler's
/// <see cref="StaticTypeRegistry"/> carries 35 rungs and the editor's own pickers write bare aliases
/// (<c>"int"</c>) that never equalled the canonical FQNs (<c>"System.Int32"</c>) recipes/literals use.
/// These tests lock the fix: both sides resolve through <c>StaticTypeRegistry.Instance.TryResolve</c>
/// and compare <c>IrTypeRef.FullName</c>, then delegate coercion to
/// <c>StaticTypeRegistry.Instance.TryGetCoercion</c> — mirroring
/// <c>Stage4_TypeResolve.VerifyLinkTypes</c> rung for rung.
/// </summary>
public sealed class BP203_WiringTypeCompatibilityTests
{
    private static BlueprintTypeSystem MakeSut()
        => new(NullPinDefaultValueEditorRegistry.Instance);

    // ── 1. Headline case: alias vs FQN spellings of the SAME type must wire ───────────────

    [Theory]
    [InlineData("int", "System.Int32")]
    [InlineData("float", "System.Single")]
    [InlineData("bool", "System.Boolean")]
    [InlineData("FixedString32", "Fdp.Core.FixedString32")]
    public void AliasAndFqn_SameType_AreCompatible_BothDirections(string alias, string fqn)
    {
        var sut = MakeSut();
        // Before BP-203 this was refused: the raw strings differ even though they name one type.
        Assert.True(sut.AreCompatible(new TypeKey(alias), new TypeKey(fqn)));
        Assert.True(sut.AreCompatible(new TypeKey(fqn), new TypeKey(alias)));
    }

    // ── 2. ⭐ Parity test — the durable half. Driven from StaticTypeRegistry itself, not a
    // hand-written list, so it goes red the moment anyone re-adds a hand-maintained coercion rung
    // to BlueprintTypeSystem instead of delegating to StaticTypeRegistry. ────────────────────────

    private static bool CompilerSaysCompatible(string fromId, string toId)
    {
        // Mirrors Stage4_TypeResolve.VerifyLinkTypes exactly (see BlueprintTypeSystem.AreCompatible
        // XML doc): resolve both; compatible iff same FullName, OR a coercion rung exists, OR either
        // resolved FullName is the System.Object wildcard.
        var fromOk = StaticTypeRegistry.Instance.TryResolve(new BlueprintTypeRef { TypeId = fromId }, out var fromIr);
        var toOk   = StaticTypeRegistry.Instance.TryResolve(new BlueprintTypeRef { TypeId = toId },   out var toIr);
        Assert.True(fromOk, $"'{fromId}' (drawn from EditorOfferableTypeIds) must resolve.");
        Assert.True(toOk,   $"'{toId}' (drawn from EditorOfferableTypeIds) must resolve.");

        if (fromIr.FullName == toIr.FullName) return true;
        if (StaticTypeRegistry.Instance.TryGetCoercion(fromIr, toIr, out _)) return true;
        if (fromIr.FullName == "System.Object" || toIr.FullName == "System.Object") return true;
        return false;
    }

    [Fact]
    public void AreCompatible_MatchesCompilerCoercionRules_ForEveryEditorOfferableTypePair()
    {
        var sut = MakeSut();

        // Both spellings of every editor-offerable type: the alias the picker writes AND the
        // canonical FullName it resolves to (recipes/literals carry the latter).
        var ids = new List<string>();
        foreach (var alias in StaticTypeRegistry.EditorOfferableTypeIds)
        {
            ids.Add(alias);
            if (StaticTypeRegistry.Instance.TryResolve(new BlueprintTypeRef { TypeId = alias }, out var ir))
                ids.Add(ir.FullName);
        }
        ids = ids.Distinct(StringComparer.Ordinal).ToList();

        var mismatches = new List<string>();
        foreach (var from in ids)
        {
            foreach (var to in ids)
            {
                var expected = CompilerSaysCompatible(from, to);
                var actual   = sut.AreCompatible(new TypeKey(from), new TypeKey(to));
                if (expected != actual)
                    mismatches.Add($"'{from}' -> '{to}': compiler says {expected}, editor says {actual}");
            }
        }

        Assert.True(mismatches.Count == 0,
            $"Editor/compiler compatibility drift found ({mismatches.Count} pair(s)):\n" + string.Join("\n", mismatches));
    }

    // ── 3. BP-87 named wires: uint <-> ushort <-> int, and the byte->int rung ────────────────

    [Fact]
    public void UShort_To_Int_IsCompatible()
    {
        // BP-87's explicit ruling: "wiring possible between uint <-> ushort <-> int pins".
        var sut = MakeSut();
        Assert.True(sut.AreCompatible(new TypeKey("ushort"), new TypeKey("int")));
    }

    [Fact]
    public void UShort_To_UInt_IsCompatible()
    {
        var sut = MakeSut();
        Assert.True(sut.AreCompatible(new TypeKey("ushort"), new TypeKey("uint")));
    }

    [Fact]
    public void UInt_To_Long_IsCompatible()
    {
        var sut = MakeSut();
        Assert.True(sut.AreCompatible(new TypeKey("uint"), new TypeKey("long")));
    }

    [Fact]
    public void Byte_To_Int_IsCompatible()
    {
        var sut = MakeSut();
        Assert.True(sut.AreCompatible(new TypeKey("byte"), new TypeKey("int")));
    }

    // ── 4. Narrowing is still refused: no rung exists, C# demands an explicit cast ───────────

    [Theory]
    [InlineData("int", "ushort")]
    [InlineData("long", "int")]
    [InlineData("double", "float")]
    [InlineData("float", "int")]
    public void Narrowing_IsNotCompatible(string from, string to)
    {
        var sut = MakeSut();
        Assert.False(sut.AreCompatible(new TypeKey(from), new TypeKey(to)));
    }

    // ── 5. Exec pins: TypeKey.Empty is its own island ────────────────────────────────────────

    [Fact]
    public void ExecPin_IsCompatible_WithExecPin()
    {
        var sut = MakeSut();
        Assert.True(sut.AreCompatible(TypeKey.Empty, TypeKey.Empty));
    }

    [Fact]
    public void ExecPin_IsNotCompatible_WithAnyDataType_BothDirections()
    {
        var sut  = MakeSut();
        var data = new TypeKey(BlueprintTypeSystem.Int32);
        // ⚠ The fix must never let alias resolution / coercion / the System.Object wildcard swallow
        // the exec-vs-data kind split -- exec pins carry TypeKey.Empty (Id ""), which never resolves.
        Assert.False(sut.AreCompatible(TypeKey.Empty, data));
        Assert.False(sut.AreCompatible(data, TypeKey.Empty));
    }

    // ── 6. System.Object wildcard survives in both directions ───────────────────────────────

    [Fact]
    public void ObjectWildcard_AcceptsAndIsAcceptedBy_RealDataType_BothDirections()
    {
        var sut    = MakeSut();
        var obj    = new TypeKey("System.Object");
        var intKey = new TypeKey(BlueprintTypeSystem.Int32);
        Assert.True(sut.AreCompatible(intKey, obj));
        Assert.True(sut.AreCompatible(obj, intKey));
    }

    // ── 7. IsImplicitCast: exactly (differ AND coercion rung exists) ────────────────────────

    [Fact]
    public void IsImplicitCast_IntToFloat_IsTrue()
    {
        var sut = MakeSut();
        Assert.True(sut.IsImplicitCast(new TypeKey("int"), new TypeKey("float")));
    }

    [Fact]
    public void IsImplicitCast_UShortToInt_IsTrue()
    {
        var sut = MakeSut();
        Assert.True(sut.IsImplicitCast(new TypeKey("ushort"), new TypeKey("int")));
    }

    [Fact]
    public void IsImplicitCast_IdenticalType_IsFalse()
    {
        var sut = MakeSut();
        Assert.False(sut.IsImplicitCast(new TypeKey("float"), new TypeKey("float")));
    }

    [Fact]
    public void IsImplicitCast_AliasVsFqn_SameType_IsFalse()
    {
        // "int" -> "System.Int32" is not a cast -- it is the same type in two spellings.
        var sut = MakeSut();
        Assert.False(sut.IsImplicitCast(new TypeKey("int"), new TypeKey("System.Int32")));
        Assert.False(sut.IsImplicitCast(new TypeKey("System.Int32"), new TypeKey("int")));
    }

    [Fact]
    public void IsImplicitCast_ExecPin_IsFalse()
    {
        var sut = MakeSut();
        Assert.False(sut.IsImplicitCast(TypeKey.Empty, new TypeKey(BlueprintTypeSystem.Int32)));
    }

    [Theory]
    [InlineData("int", "ushort")]
    [InlineData("float", "int")]
    public void IsImplicitCast_Narrowing_IsFalse(string from, string to)
    {
        var sut = MakeSut();
        Assert.False(sut.IsImplicitCast(new TypeKey(from), new TypeKey(to)));
    }

    // ── 8. Display half: TryGetTypeInfo / GetPinColor now resolve aliases too ───────────────
    // Before BP-203 the palette was keyed by FQN only, so an alias-spelled pin fell through to the
    // unnamed grey "unknown" default even though the identical FQN-spelled pin rendered named+colored.

    [Fact]
    public void TryGetTypeInfo_AliasAndFqn_ProduceSameDisplayName_Int()
    {
        var sut = MakeSut();
        Assert.True(sut.TryGetTypeInfo(new TypeKey("int"), out var aliasInfo));
        Assert.True(sut.TryGetTypeInfo(new TypeKey("System.Int32"), out var fqnInfo));
        Assert.Equal(fqnInfo.DisplayName, aliasInfo.DisplayName);
    }

    [Fact]
    public void TryGetTypeInfo_AliasAndFqn_ProduceSameDisplayName_FixedString32()
    {
        var sut = MakeSut();
        Assert.True(sut.TryGetTypeInfo(new TypeKey("FixedString32"), out var aliasInfo));
        Assert.True(sut.TryGetTypeInfo(new TypeKey("Fdp.Core.FixedString32"), out var fqnInfo));
        Assert.Equal(fqnInfo.DisplayName, aliasInfo.DisplayName);
    }

    [Fact]
    public void GetPinColor_AliasAndFqn_ProduceSameColor_Int()
    {
        var sut = MakeSut();
        var aliasColor = sut.GetPinColor(new TypeKey("int"));
        var fqnColor   = sut.GetPinColor(new TypeKey("System.Int32"));
        Assert.Equal(fqnColor, aliasColor);
        // Sanity: not the unknown-grey fallback (grey is roughly equal R/G/B around 0.8).
        Assert.False(Math.Abs(aliasColor.X - 0.8f) < 0.01f && Math.Abs(aliasColor.Y - 0.8f) < 0.01f
            && Math.Abs(aliasColor.Z - 0.8f) < 0.01f);
    }

    [Fact]
    public void GetPinColor_AliasAndFqn_ProduceSameColor_FixedString32()
    {
        var sut = MakeSut();
        var aliasColor = sut.GetPinColor(new TypeKey("FixedString32"));
        var fqnColor   = sut.GetPinColor(new TypeKey("Fdp.Core.FixedString32"));
        Assert.Equal(fqnColor, aliasColor);
    }
}
