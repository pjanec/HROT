using System.Text.Json.Nodes;
using Hrot.Blueprints.Core;

namespace Hrot.Blueprints.Tests.Golden;

/// <summary>
/// ⭐⭐ <b><c>BP-240</c>, asked of the migration.</b>
///
/// <para>
/// ⛔ <b>The habit this file exists to apply.</b> Batch 53 broke the store's grouping invariant and
/// <b>both</b> <c>persistence-shape</c> and golden stayed green — not because the invariant was
/// unimportant, but because all 42 corpus assets exercise exactly one traversal of the model.
/// ⇒ 📐 <b>The same question here: what do <c>Up</c>/<c>Down</c> do correctly only because all 58
/// shipped files happen to be shaped a certain way?</b>
/// </para>
///
/// <para>
/// ⭐ <b><c>V1ToV2ToV1IsTheIdentity_ForEveryShippedAsset</c> is a strong gate over weak inputs.</b>
/// Every one of those 58 files is <b>canonical</b> — written by <c>BlueprintJsonServices</c>, so it
/// carries all three declaration lists, as arrays, in model order, each followed by its <c>*Order</c>.
/// ⛔ <b>Nothing in the corpus can tell you what happens to a document that is none of those things</b>
/// — and a hand-authored file, or one written by an older tool, easily is.
/// </para>
///
/// <para>
/// 📌 <b>These fixtures are constructed, and that is the point.</b> Each one is a shape the writer
/// never produces.
/// </para>
/// </summary>
public sealed class SchemaV2AdversarialTests
{
    private static readonly System.Text.Json.JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    private static string RoundTrip(JsonObject v1)
        => BlueprintSchemaV2.Down(BlueprintSchemaV2.Up(v1)).ToJsonString(Indented);

    private static string Text(JsonObject o) => o.ToJsonString(Indented);

    private const string Decl = @"{ ""Id"": ""11111111-1111-1111-1111-111111111111"", ""Name"": ""X"" }";

    // ── the shapes the corpus DOES produce, as controls ─────────────────────

    /// <summary>
    /// ⭐ The canonical shape — all three lists present, in model order, each followed by its order
    /// list. ⚠ A control: if this ever fails, the fixtures below say nothing.
    /// </summary>
    [Fact]
    public void TheCanonicalShapeRoundTripsExactly()
    {
        var v1 = Obj(@"{
  ""Name"": ""Canonical"",
  ""Parameters"": [" + Decl + @"],
  ""ParameterOrder"": null,
  ""WorkingState"": [],
  ""WorkingStateOrder"": null,
  ""Variables"": [],
  ""VariableOrder"": null,
  ""Graphs"": []
}");
        Assert.Equal(Text(v1), RoundTrip(v1));
    }

    /// <summary>⭐ Zero declarations of every kind — the three Library assets' shape.</summary>
    [Fact]
    public void AnAssetDeclaringNothingRoundTripsExactly()
    {
        var v1 = Obj(@"{
  ""Name"": ""Empty"",
  ""Parameters"": [],
  ""ParameterOrder"": null,
  ""WorkingState"": [],
  ""WorkingStateOrder"": null,
  ""Variables"": [],
  ""VariableOrder"": null
}");
        Assert.Equal(Text(v1), RoundTrip(v1));
    }

    // ── the shapes the corpus CANNOT produce ────────────────────────────────

    /// <summary>
    /// ⛔⛔ <b>A declaration list is ABSENT.</b> <c>Up</c> skips what it cannot find; <c>Down</c> always
    /// emits all three. ⇒ the round trip <b>invents</b> the missing property.
    ///
    /// <para>
    /// ⚠ <b>Genuinely lossy, and not fixable inside <c>Down</c>:</b> v2 has one array, so "this kind
    /// had no entries" and "this kind's property was absent" are the same document. ⭐ The fix is
    /// therefore at the <c>Up</c> end — refuse the input — not at the <c>Down</c> end.
    /// </para>
    /// </summary>
    [Fact]
    public void AnAbsentDeclarationListIsRefusedRatherThanInvented()
    {
        var v1 = Obj(@"{ ""Name"": ""NoWorkingState"", ""Parameters"": [], ""Variables"": [] }");

        var ex = Assert.Throws<InvalidDataException>(() => BlueprintSchemaV2.Up(v1));
        Assert.Contains("WorkingState", ex.Message);
    }

    /// <summary>
    /// ⛔ <b>A declaration list is <c>null</c> rather than an array.</b> Same asymmetry: <c>Up</c>'s
    /// <c>is not JsonArray</c> guard treats null exactly like absent, and <c>Down</c> would write
    /// <c>[]</c> back.
    /// </summary>
    [Fact]
    public void ANullDeclarationListIsRefusedRatherThanTurnedIntoAnEmptyOne()
    {
        var v1 = Obj(@"{ ""Name"": ""NullList"", ""Parameters"": null, ""WorkingState"": [], ""Variables"": [] }");

        var ex = Assert.Throws<InvalidDataException>(() => BlueprintSchemaV2.Up(v1));
        Assert.Contains("Parameters", ex.Message);
    }

    /// <summary>
    /// ⭐⭐ <b><c>BP-240</c>'s exact shape, at the file level: the three lists in an order the writer
    /// never produces.</b>
    ///
    /// <para>
    /// <c>Up</c> puts the union where the <b>first</b> of the three lists sat — here <c>Variables</c> —
    /// and <c>Down</c> restores all three in <b>model</b> order at that slot. ⇒ the bytes move even
    /// though nothing is lost. ⛔ The 58-file gate cannot see this: every one of them is already in
    /// model order.
    /// </para>
    ///
    /// <para>
    /// ⚖️ <b>Ruled a REFUSAL rather than a repair.</b> Restoring an arbitrary original property order
    /// would mean <c>Up</c> recording layout in the v2 document — carrying a v1 artefact into v2 for a
    /// shape no writer emits. ⭐ Refusing says the true thing: <c>Up</c> takes canonical v1 in.
    /// </para>
    /// </summary>
    [Fact]
    public void DeclarationListsOutOfModelOrderAreRefused()
    {
        var v1 = Obj(@"{
  ""Name"": ""OutOfOrder"",
  ""Variables"": [],
  ""VariableOrder"": null,
  ""Parameters"": [],
  ""ParameterOrder"": null,
  ""WorkingState"": [],
  ""WorkingStateOrder"": null
}");

        var ex = Assert.Throws<InvalidDataException>(() => BlueprintSchemaV2.Up(v1));
        Assert.Contains("order", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⭐ <b>An <c>*Order</c> list naming an id that no declaration carries.</b> <c>BP-231</c> made this
    /// rarer; a hand-authored file can still do it. ⚠ Both transforms treat the order lists as opaque
    /// values, so this must survive untouched — ⛔ <b>silently dropping a stale id would be a
    /// migration quietly editing the designer's ordering.</b>
    /// </summary>
    [Fact]
    public void AStaleIdInAnOrderListSurvivesUntouched()
    {
        var v1 = Obj(@"{
  ""Name"": ""StaleOrder"",
  ""Parameters"": [],
  ""ParameterOrder"": null,
  ""WorkingState"": [],
  ""WorkingStateOrder"": null,
  ""Variables"": [" + Decl + @"],
  ""VariableOrder"": [""99999999-9999-9999-9999-999999999999""]
}");
        Assert.Equal(Text(v1), RoundTrip(v1));
    }

    /// <summary>
    /// ⭐ <b>A name collision across kinds — the shape <c>BP1673</c> refuses at Stage 2.</b>
    /// ⛔ The migrator must still read it: a migration that only works on assets that already compile
    /// cannot be used to fix the ones that do not.
    /// </summary>
    [Fact]
    public void AnAssetWithACrossKindNameCollisionStillMigrates()
    {
        var v1 = Obj(@"{
  ""Name"": ""Collision"",
  ""Parameters"": [],
  ""ParameterOrder"": null,
  ""WorkingState"": [{ ""Id"": ""22222222-2222-2222-2222-222222222222"", ""Name"": ""Health"" }],
  ""WorkingStateOrder"": null,
  ""Variables"": [{ ""Id"": ""33333333-3333-3333-3333-333333333333"", ""Name"": ""Health"" }],
  ""VariableOrder"": null
}");
        Assert.Equal(Text(v1), RoundTrip(v1));

        // ⭐ And both survive the trip as distinct, correctly-tagged entries.
        var v2 = BlueprintSchemaV2.Up(v1);
        var declarations = v2[BlueprintSchemaV2.DeclarationsProperty]!.AsArray();
        Assert.Equal(2, declarations.Count);
        Assert.Equal("WorkingState", declarations[0]![BlueprintSchemaV2.KindProperty]!.GetValue<string>());
        Assert.Equal("Variable",     declarations[1]![BlueprintSchemaV2.KindProperty]!.GetValue<string>());
    }

    /// <summary>
    /// ⭐ <b>A declaration carrying a property the model does not know.</b> The transform is a DOM
    /// operation, so an unknown member must pass through — ⛔ a migration that quietly drops fields it
    /// does not recognise is how a forward-compatible format stops being one.
    /// </summary>
    [Fact]
    public void AnUnknownPropertyOnADeclarationSurvivesBothDirections()
    {
        var v1 = Obj(@"{
  ""Name"": ""Unknown"",
  ""Parameters"": [],
  ""ParameterOrder"": null,
  ""WorkingState"": [],
  ""WorkingStateOrder"": null,
  ""Variables"": [{ ""Id"": ""44444444-4444-4444-4444-444444444444"", ""Name"": ""V"", ""FutureThing"": 7 }],
  ""VariableOrder"": null
}");
        Assert.Equal(Text(v1), RoundTrip(v1));
    }

    /// <summary>
    /// ⛔ <b>A declaration whose own <c>Kind</c> property collides with the tag.</b> <c>Up</c> writes
    /// the tag first and then copies the declaration's members over it, so a v1 declaration carrying
    /// its own <c>Kind</c> would <b>overwrite the tag</b> — and <c>Down</c> would then partition by the
    /// wrong value, or throw. ⭐ Refused at the boundary instead, with the reason named.
    /// </summary>
    [Fact]
    public void ADeclarationCarryingItsOwnKindPropertyIsRefused()
    {
        var v1 = Obj(@"{
  ""Name"": ""KindClash"",
  ""Parameters"": [],
  ""ParameterOrder"": null,
  ""WorkingState"": [],
  ""WorkingStateOrder"": null,
  ""Variables"": [{ ""Id"": ""55555555-5555-5555-5555-555555555555"", ""Name"": ""V"", ""Kind"": ""Parameter"" }],
  ""VariableOrder"": null
}");

        var ex = Assert.Throws<InvalidDataException>(() => BlueprintSchemaV2.Up(v1));
        Assert.Contains(BlueprintSchemaV2.KindProperty, ex.Message);
    }
}
