using System;
using System.Collections.Generic;
using Fdp.Toolkit.Replication.Patching;
using Hrot.NED.Messages;

namespace Hrot.SimHost.Installers;

/// <summary>
/// ⭐⭐⭐ <b><c>R-134</c>'s SOLE BOUNDARY — the one place a DDS attribute record and an FDP-internal one meet.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §11.1–§11.3 · <c>RULINGS.md</c> <c>R-134</c>:
/// 🔒 *"No DDS type crosses into the FDP-internal path; the egress translator is the SOLE boundary."*</para>
///
/// <para>⭐⭐ <b>Both directions live here, together, on purpose.</b> ⛔ Splitting them across the ingress
/// system and the egress translator is how a wire format grows two disagreeing halves: a value written by
/// one and read by the other must round-trip, and that is far easier to see — and to rail — when the two
/// conversions sit side by side. ⚠ It is still ONE boundary: nothing else in the codebase may call these.</para>
///
/// <para>⭐⭐⭐ <b>Why the enum mapping is written out rather than cast.</b> <see cref="AttributeValueKind"/>
/// and <see cref="AttributeValueType"/> are numerically identical *(and now explicitly so)*, so
/// <c>(AttributeValueType)kind</c> would work today. ⛔ **A cast would silently follow either enum if it
/// ever moved** — exactly the failure the duplication exists to make impossible. ⭐ An explicit
/// <c>switch</c> turns that into a compile error on a new member and a rail failure on a renumber.</para>
/// </summary>
public static class AttributeRecordConversion
{
    // ══ enum: internal ⇄ network ═════════════════════════════════════════════════

    /// <summary>⭐ FDP-internal kind → the network type. ⛔ Explicit, never a cast — see the class remarks.</summary>
    public static AttributeValueType ToNetwork(AttributeValueKind kind) => kind switch
    {
        AttributeValueKind.CsInt32   => AttributeValueType.KindInt32,
        AttributeValueKind.CsInt64   => AttributeValueType.KindInt64,
        AttributeValueKind.CsFloat32 => AttributeValueType.KindFloat32,
        AttributeValueKind.CsFloat64 => AttributeValueType.KindFloat64,
        AttributeValueKind.Bool      => AttributeValueType.KindBool,
        AttributeValueKind.CsString  => AttributeValueType.KindString,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "Unmapped AttributeValueKind — add the arm rather than casting."),
    };

    /// <summary>⭐ The network type → the FDP-internal kind.</summary>
    public static AttributeValueKind ToInternal(AttributeValueType type) => type switch
    {
        AttributeValueType.KindInt32   => AttributeValueKind.CsInt32,
        AttributeValueType.KindInt64   => AttributeValueKind.CsInt64,
        AttributeValueType.KindFloat32 => AttributeValueKind.CsFloat32,
        AttributeValueType.KindFloat64 => AttributeValueKind.CsFloat64,
        AttributeValueType.KindBool    => AttributeValueKind.Bool,
        AttributeValueType.KindString  => AttributeValueKind.CsString,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "Unmapped AttributeValueType — add the arm rather than casting."),
    };

    // ══ record: network → internal (the INGRESS boundary) ════════════════════════

    /// <summary>⭐ One DDS record → one FDP-internal change.</summary>
    public static EntityAttributeChange ToInternal(AttributeRecord record) => new()
    {
        AttributeId = record.AttributeId,
        SubIndex1   = record.SubIndex1,
        SubIndex2   = record.SubIndex2,
        Value       = new AttributeValue
        {
            Kind        = ToInternal(record.Value.ValueType),
            IntValue    = record.Value.IntValue,
            LongValue   = record.Value.LongValue,
            FloatValue  = record.Value.FloatValue,
            DoubleValue = record.Value.DoubleValue,
            BoolValue   = record.Value.BoolValue,
            StringValue = record.Value.StringValue,
        },
    };

    /// <summary>
    /// ⭐ A DDS record list → an FDP-internal array the interpreter can apply.
    ///
    /// <para>⚠ Allocates one array per call. 📐 Per REQUEST, not per tick — an operator gesture or a
    /// script call. ⭐ It replaced a zero-copy span over the DDS list, and that trade is <c>R-134</c>'s
    /// price: the alternative is the wire type being the interpreter's record type.</para>
    /// </summary>
    public static EntityAttributeChange[] ToInternal(IReadOnlyList<AttributeRecord>? records)
    {
        if (records == null || records.Count == 0) return Array.Empty<EntityAttributeChange>();

        var result = new EntityAttributeChange[records.Count];
        for (int i = 0; i < records.Count; i++) result[i] = ToInternal(records[i]);
        return result;
    }

    // ══ record: internal → network (the EGRESS boundary) ═════════════════════════

    /// <summary>⭐ One FDP-internal change → one DDS record, for the request egress translator.</summary>
    public static AttributeRecord ToNetwork(EntityAttributeChange change) => new()
    {
        AttributeId = change.AttributeId,
        SubIndex1   = change.SubIndex1,
        SubIndex2   = change.SubIndex2,
        Value       = new AttributeValueUnion
        {
            ValueType   = ToNetwork(change.Value.Kind),
            IntValue    = change.Value.IntValue,
            LongValue   = change.Value.LongValue,
            FloatValue  = change.Value.FloatValue,
            DoubleValue = change.Value.DoubleValue,
            BoolValue   = change.Value.BoolValue,
            StringValue = change.Value.StringValue,
        },
    };
}
