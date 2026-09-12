namespace Fdp.Toolkit.Replication.Patching;

/// <summary>
/// ⭐⭐⭐ <b>The FDP-INTERNAL attribute value — <c>R-134</c>'s internal half.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §11.1–§11.3 · <c>RULINGS.md</c> <c>R-134</c>.</para>
///
/// <para>⛔⛔ <b>Mirrors <c>Hrot.NED.Messages.AttributeValueUnion</c> deliberately, and must NOT be replaced
/// by it.</b> 🔒 *"No DDS type crosses into the FDP-internal path; the egress translator is the SOLE
/// boundary."* ⭐ The member names match the union's on purpose, so the conversion at the boundary reads as
/// a rename rather than a remap, and an installer moved between the two needs no rethinking.</para>
///
/// <para>⚠ <b>One storage field per width, not a real union.</b> ⭐ Deliberate: this type lives on the FDP
/// bus and in operator-gesture code paths, ⛔ not in a per-tick hot loop — clarity beats the twelve bytes a
/// hand-packed layout would save, and a mis-read discriminant on a true union is a silent wrong value.</para>
/// </summary>
public readonly struct AttributeValue
{
    /// <summary>⭐ Which field below is meaningful. The FDP-internal enum — ⛔ never the network one.</summary>
    public AttributeValueKind Kind { get; init; }

    /// <summary>Valid when <see cref="Kind"/> is <see cref="AttributeValueKind.CsInt32"/>.</summary>
    public int IntValue { get; init; }

    /// <summary>Valid when <see cref="Kind"/> is <see cref="AttributeValueKind.CsInt64"/>.</summary>
    public long LongValue { get; init; }

    /// <summary>Valid when <see cref="Kind"/> is <see cref="AttributeValueKind.CsFloat32"/>.</summary>
    public float FloatValue { get; init; }

    /// <summary>Valid when <see cref="Kind"/> is <see cref="AttributeValueKind.CsFloat64"/>.</summary>
    public double DoubleValue { get; init; }

    /// <summary>Valid when <see cref="Kind"/> is <see cref="AttributeValueKind.Bool"/>.</summary>
    public bool BoolValue { get; init; }

    /// <summary>Valid when <see cref="Kind"/> is <see cref="AttributeValueKind.CsString"/>.</summary>
    public string? StringValue { get; init; }

    /// <summary>⭐ A 64-bit float value — the <c>Geo*</c> family's shape.</summary>
    public static AttributeValue FromDouble(double value)
        => new() { Kind = AttributeValueKind.CsFloat64, DoubleValue = value };

    /// <summary>⭐ A 32-bit float value.</summary>
    public static AttributeValue FromFloat(float value)
        => new() { Kind = AttributeValueKind.CsFloat32, FloatValue = value };

    /// <summary>⭐ A 32-bit integer value.</summary>
    public static AttributeValue FromInt(int value)
        => new() { Kind = AttributeValueKind.CsInt32, IntValue = value };

    /// <summary>⭐ A 64-bit integer value.</summary>
    public static AttributeValue FromLong(long value)
        => new() { Kind = AttributeValueKind.CsInt64, LongValue = value };

    /// <summary>⭐ A boolean value.</summary>
    public static AttributeValue FromBool(bool value)
        => new() { Kind = AttributeValueKind.Bool, BoolValue = value };

    /// <summary>⭐ A string value.</summary>
    public static AttributeValue FromString(string? value)
        => new() { Kind = AttributeValueKind.CsString, StringValue = value };
}

/// <summary>
/// ⭐⭐⭐ <b>One attribute change, in FDP-INTERNAL terms — what the interpreter applies and what the write
/// router speaks.</b>
///
/// <para>📄 <c>DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §11.3. ⛔ The DDS counterpart is
/// <c>Hrot.NED.Messages.AttributeRecord</c>, and it appears ONLY inside the ingress/egress translators
/// *(<c>R-134</c>)*.</para>
///
/// <para>⭐⭐ <b>This type is now the interpreter's record type</b>, so the installers — and therefore the
/// single implementation of every attribute's conversion — are FDP-internal too. ⇒ ⭐ a DDS
/// <c>AttributeRecord</c> is converted to this **on the way in**, and this is converted to a DDS record
/// **on the way out**; nothing in between knows the wire exists.</para>
///
/// <para>⚠ <b><c>SubIndex1</c>/<c>SubIndex2</c> are carried even though no shipped installer reads them</b>
/// *(measured <c>2026-08-25</c>)*. ⛔ Dropping them at the conversion would silently discard list-position
/// information a future nested attribute needs, and a silent loss on the receive path is the worst kind.</para>
/// </summary>
public readonly struct EntityAttributeChange
{
    /// <summary>The well-known attribute id — see <see cref="AttributeIds"/>.</summary>
    public ushort AttributeId { get; init; }

    /// <summary>First optional sub-index (e.g. list position). Zero when unused.</summary>
    public short SubIndex1 { get; init; }

    /// <summary>Second optional sub-index. Zero when unused.</summary>
    public short SubIndex2 { get; init; }

    /// <summary>The typed value.</summary>
    public AttributeValue Value { get; init; }

    /// <summary>⭐ The common case: an attribute carrying a 64-bit float.</summary>
    public static EntityAttributeChange Double(ushort attributeId, double value)
        => new() { AttributeId = attributeId, Value = AttributeValue.FromDouble(value) };
}
