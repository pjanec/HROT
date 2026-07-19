using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Editor punch-list #4 — builds the hover tooltip shown on a <see cref="FunctionCallNode"/>.
///
/// <para>
/// The tooltip always leads with the <b>signature</b> reconstructed from the node's already-resolved
/// pins (<c>ReturnType MethodName(paramType paramName, …)</c>) — so the data types are present with
/// zero reflection risk (the mandatory requirement) — and appends the CLR method's XML-doc
/// <c>&lt;summary&gt;</c> when one is available on disk (see <see cref="ClrXmlDocSource"/>).
/// </para>
/// </summary>
internal static class FunctionCallTooltip
{
    /// <summary>
    /// Builds the tooltip text for <paramref name="fc"/> from its projected <paramref name="pins"/>.
    /// Returns <c>null</c> when there is nothing useful to show (no method name and no pins).
    /// </summary>
    public static string? Build(FunctionCallNode fc, IReadOnlyList<IPinModel> pins)
    {
        var name = string.IsNullOrEmpty(fc.MethodName) ? "Function" : fc.MethodName;

        // Data-IN pins are the (wireable) parameters, in pin order; data-OUT is the return slot.
        var paramParts = new List<string>();
        string? returnType = null;
        foreach (var pin in pins)
        {
            if (pin.Kind != PinKind.Data) continue;
            if (pin.Direction == PinDirection.Input)
                paramParts.Add($"{ShortType(pin.Type)} {pin.Label}");
            else if (returnType == null) // first data-out is the Return value
                returnType = ShortType(pin.Type);
        }

        var signature = returnType == null
            ? $"{name}({string.Join(", ", paramParts)})"
            : $"{returnType} {name}({string.Join(", ", paramParts)})";

        var body = signature;

        // Kind line: CLR library method vs in-blueprint function graph.
        var kind = !string.IsNullOrEmpty(fc.TargetGraphId)
            ? "Blueprint function"
            : !string.IsNullOrEmpty(fc.TargetTypeId)
                ? $"CLR method — {fc.TargetTypeId}"
                : null;
        if (kind != null)
            body += "\n" + kind;

        // XML-doc summary (CLR mode only; disk artifact, absent for graph calls / dynamic asms).
        var method = NodePinSchema.ResolveClrMethod(fc);
        if (method != null)
        {
            var summary = ClrXmlDocSource.GetSummary(method);
            if (!string.IsNullOrEmpty(summary))
                body += "\n\n" + summary;
        }

        return body;
    }

    /// <summary>Short, display-friendly type name from a pin's <see cref="TypeKey"/> (strips namespace + <c>global::</c>).</summary>
    private static string ShortType(TypeKey? type)
    {
        var id = type?.Id;
        if (string.IsNullOrEmpty(id)) return "object";
        return TooltipText.ShortTypeName(id);
    }
}

/// <summary>Small shared helpers for tooltip text formatting.</summary>
internal static class TooltipText
{
    /// <summary>
    /// Reduces a pin TypeId to a readable short name: drops the <c>global::</c> AN2 sentinel and
    /// the namespace, keeping the last dotted segment (e.g. <c>System.Numerics.Vector3</c> → <c>Vector3</c>).
    /// </summary>
    public static string ShortTypeName(string typeId)
    {
        if (string.IsNullOrEmpty(typeId)) return "object";
        var s = typeId;
        if (s.StartsWith("global::", StringComparison.Ordinal)) s = s["global::".Length..];
        var dot = s.LastIndexOf('.');
        return dot >= 0 && dot < s.Length - 1 ? s[(dot + 1)..] : s;
    }
}
