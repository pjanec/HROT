using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Editor.Variables;

namespace Hrot.Blueprints.Tests.Catalog;

/// <summary>
/// BP-87: the blueprint type picker used to offer <b>eight types the compiler could not resolve</b> —
/// <c>sbyte ushort uint ulong</c> (registered under no name at all) and
/// <c>Vector2 Vector3 Vector4 Quaternion</c> (registered under their FQN only). Choosing one produced
/// an asset the editor itself could not compile (BP1500).
///
/// <para>
/// ⭐ These tests are the durable half of the fix. Registering the missing types is a table edit that
/// the next hand-edit can silently undo; what stops the drift returning is
/// <see cref="OfferedTypes_AllResolve"/> — the picker's list and the compiler's type table can no
/// longer disagree without a red test.
/// </para>
/// </summary>
public sealed class BP87_TypePickerTests
{
    private static IrTypeRefOrNull Resolve(string typeId)
    {
        var ok = StaticTypeRegistry.Instance.TryResolve(
            new BlueprintTypeRef { TypeId = typeId }, out var ir);
        return new IrTypeRefOrNull(ok, ok ? ir.FullName : null);
    }

    private readonly record struct IrTypeRefOrNull(bool Resolved, string? FullName);

    // ── item 5: the anti-drift lock ───────────────────────────────────────────

    [Fact]
    public void OfferedTypes_AllResolve()
    {
        var unresolvable = BlueprintTypeChoices.TypeIds
            .Where(id => !Resolve(id).Resolved)
            .ToList();

        Assert.True(unresolvable.Count == 0,
            "the type picker must never offer a type the compiler cannot resolve — that is BP-87 " +
            "itself, and it produces an asset the editor cannot compile. Unresolvable: " +
            string.Join(", ", unresolvable));
    }

    [Fact]
    public void OfferedTypes_ComeFromTheCompilersOwnRegistry()
    {
        // The projection, not a copy: a second hand-maintained array is how BP-87 happened.
        Assert.Same(StaticTypeRegistry.EditorOfferableTypeIds, BlueprintTypeChoices.TypeIds);
    }

    // ── items 1-3: the specific types the user asked for ──────────────────────

    [Theory]
    [InlineData("sbyte",  "System.SByte")]
    [InlineData("ushort", "System.UInt16")]
    [InlineData("uint",   "System.UInt32")]
    [InlineData("ulong",  "System.UInt64")]
    public void UnsignedAliases_ResolveToTheCanonicalPrimitive(string alias, string expected)
    {
        Assert.Contains(alias, BlueprintTypeChoices.TypeIds);
        Assert.Equal(expected, Resolve(alias).FullName);
    }

    [Theory]
    [InlineData("Vector2",    "System.Numerics.Vector2")]
    [InlineData("Vector3",    "System.Numerics.Vector3")]
    [InlineData("Vector4",    "System.Numerics.Vector4")]
    [InlineData("Quaternion", "System.Numerics.Quaternion")]
    public void BareVectorAliases_ResolveToTheirFqn(string alias, string expected)
    {
        Assert.Contains(alias, BlueprintTypeChoices.TypeIds);
        Assert.Equal(expected, Resolve(alias).FullName);
    }

    [Theory]
    [InlineData("FixedString32", "Fdp.Core.FixedString32")]
    [InlineData("FixedString64", "Fdp.Core.FixedString64")]
    public void FixedStrings_AreOfferedAndResolve(string alias, string expected)
    {
        // The one the user actually asked for: a blittable string usable in a State struct, unlike
        // System.String (BP1503).
        Assert.Contains(alias, BlueprintTypeChoices.TypeIds);
        Assert.Equal(expected, Resolve(alias).FullName);
    }

    // ── item 4: the gate on item 3 ────────────────────────────────────────────

    /// <summary>
    /// The numeric types the picker offers, and C#'s own implicit-conversion targets for each
    /// (minus <c>decimal</c>, which the registry does not carry). Stated here independently of
    /// <c>StaticTypeRegistry.CoercionTable</c> on purpose: two statements of one rule catch an edit
    /// to either.
    /// </summary>
    private static readonly (string From, string[] To)[] CSharpImplicitNumeric =
    {
        ("sbyte",  new[] { "short", "int", "long", "float", "double" }),
        ("byte",   new[] { "short", "ushort", "int", "uint", "long", "ulong", "float", "double" }),
        ("short",  new[] { "int", "long", "float", "double" }),
        ("ushort", new[] { "int", "uint", "long", "ulong", "float", "double" }),
        ("int",    new[] { "long", "float", "double" }),
        ("uint",   new[] { "long", "ulong", "float", "double" }),
        ("long",   new[] { "float", "double" }),
        ("ulong",  new[] { "float", "double" }),
        ("float",  new[] { "double" }),
        ("double", new string[0]),
    };

    [Fact]
    public void CoercionTable_MatchesCSharpsImplicitNumericConversions_Exactly()
    {
        var missing = new List<string>();
        var extra   = new List<string>();

        foreach (var (from, _) in CSharpImplicitNumeric)
        foreach (var (to,   _) in CSharpImplicitNumeric)
        {
            if (from == to) continue;

            bool cSharpAllows = CSharpImplicitNumeric.First(r => r.From == from).To.Contains(to);
            bool registryHas  = StaticTypeRegistry.Instance.TryGetCoercion(
                ResolveIr(from), ResolveIr(to), out _);

            if (cSharpAllows && !registryHas) missing.Add($"{from} -> {to}");
            // ⚠ Widening only: a rung C# itself demands a cast for (int -> uint, long -> int) would
            // be a silent lossy coercion inside a visual graph — invisible wrong values.
            if (!cSharpAllows && registryHas) extra.Add($"{from} -> {to}");
        }

        Assert.True(missing.Count == 0,
            "a numeric pair the designer can wire must have a coercion rung, or the type merely " +
            "RESOLVES and cannot be USED — the failure mode BP-87 item 4 exists to prevent. " +
            "Missing: " + string.Join(", ", missing));
        Assert.True(extra.Count == 0,
            "narrowing coercions must not be silently applied. Unexpected: " + string.Join(", ", extra));
    }

    [Fact]
    public void TheWiringTheUserAskedFor_Works()
    {
        // User's stated condition for keeping the unsigned types: "as long as it can be seamlessly
        // converted to ints (wiring possible between uint <-> ushort <-> int pins)".
        Assert.True(StaticTypeRegistry.Instance.TryGetCoercion(ResolveIr("ushort"), ResolveIr("int"), out _));
        Assert.True(StaticTypeRegistry.Instance.TryGetCoercion(ResolveIr("ushort"), ResolveIr("uint"), out _));
        Assert.True(StaticTypeRegistry.Instance.TryGetCoercion(ResolveIr("uint"),   ResolveIr("long"), out _));
    }

    private static IrTypeRef ResolveIr(string typeId)
    {
        Assert.True(StaticTypeRegistry.Instance.TryResolve(
            new BlueprintTypeRef { TypeId = typeId }, out var ir), $"'{typeId}' must resolve");
        return ir;
    }
}
