namespace Fdp.Toolkit.Replication.Patching;

/// <summary>
/// Framework-native type discriminant used by <see cref="JsonToRecordCompilerBuilder"/>
/// to indicate the expected JSON value type for each registered attribute path.
/// This enum is intentionally decoupled from any application-layer DDS wire representation.
///
/// <para>⭐⭐⭐ <b><c>R-134</c> — this is the FDP-INTERNAL half of a deliberately duplicated pair.</b> Its
/// network counterpart is <c>Hrot.NED.Messages.AttributeValueType</c>, and the two are
/// <b>numerically identical by design</b>. 🔒 <b>User, <c>2026-08-25</c>:</b> *"even at the cost of keeping
/// the same enum duplicated in two namespaces, still numerically identical"* — ⛔ **the duplication is the
/// CORRECT pattern here, not debt**: no DDS type may cross into the FDP-internal path, and the egress
/// translator is the sole boundary that converts between them.</para>
///
/// <para>⚠⚠ <b>The values are EXPLICIT since <c>2026-08-25</c>, and that is load-bearing.</b> They were
/// implicit *(0…5 by declaration order)*, which made the numeric identity with <c>AttributeValueType</c>
/// true only by accident of ordering: ⛔ inserting a member anywhere but the end would have silently
/// re-mapped every value on the wire. ⭐ Pinning them in source makes the identity a stated fact, and a
/// rail asserts it member by member.</para>
/// </summary>
public enum AttributeValueKind
{
    /// <summary>32-bit signed integer. ⭐ = <c>AttributeValueType.KindInt32</c>.</summary>
    CsInt32 = 0,

    /// <summary>64-bit signed integer. ⭐ = <c>AttributeValueType.KindInt64</c>.</summary>
    CsInt64 = 1,

    /// <summary>32-bit IEEE 754 float. ⭐ = <c>AttributeValueType.KindFloat32</c>.</summary>
    CsFloat32 = 2,

    /// <summary>64-bit IEEE 754 float. ⭐ = <c>AttributeValueType.KindFloat64</c>.</summary>
    CsFloat64 = 3,

    /// <summary>Boolean. ⭐ = <c>AttributeValueType.KindBool</c>.</summary>
    Bool = 4,

    /// <summary>UTF-8 string. ⭐ = <c>AttributeValueType.KindString</c>.</summary>
    CsString = 5,
}
