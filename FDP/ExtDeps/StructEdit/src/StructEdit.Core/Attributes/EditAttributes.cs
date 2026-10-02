namespace StructEdit.Core.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditRangeAttribute(double min, double max) : Attribute
{
    public double Min { get; } = min;
    public double Max { get; } = max;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditUnitAttribute(string unit) : Attribute
{
    public string Unit { get; } = unit;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditDisplayNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class InlineArrayHintAttribute(int length) : Attribute
{
    public int Length { get; } = length;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class FixedBufferHintAttribute(Type elementType, int length) : Attribute
{
    public Type ElementType { get; } = elementType;
    public int Length { get; } = length;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditReadOnlyAttribute : Attribute { }

/// <summary>
/// ⭐⭐⭐ <b>The free-text "how to use" half of a field's documentation.</b>
/// 📄 <c>docs/DESIGN_Mcp_Authoring.md</c> §10.6 — the ONE gap the harvest found.
///
/// <para>📐 <b>Measured:</b> the rest of this family carries <b>structural</b> doc — a display name, a
/// range, a unit, read-only-ness. ⛔ None of it says what a field MEANS or when to change it; that prose
/// lives only in XML <c>/// &lt;summary&gt;</c> comments, which are not in the assembly unless the doc-XML
/// ships alongside it. ⇒ ⭐ this is the field-granularity equivalent of <c>RouteDoc.Summary</c>: a
/// colocated descriptor, harvested at runtime, so MCP discovery can answer <i>"how do I use this?"</i>
/// from the code itself.</para>
///
/// <para>⛔⛔ <b>The alternative this exists to prevent</b> is a hand-authored doc table beside the types.
/// 📌 That is exactly the rot <c>RouteDoc</c> was built to avoid: it goes stale the first time a field is
/// renamed, and nothing fails when it does.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class
                | AttributeTargets.Struct | AttributeTargets.Method)]
public sealed class EditDocAttribute(string summary) : Attribute
{
    /// <summary>One or two sentences: what this is, and when to change it.</summary>
    public string Summary { get; } = summary;
}
