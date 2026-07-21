namespace Hrot.Editor.AiShared;

/// <summary>
/// Marks a <b><c>public static</c></b> CLR method as callable from a Blueprint <c>FunctionCall</c> node.
/// The editor reflection-scans loaded assemblies for this attribute and lists the method in the curated,
/// grouped "Add Node" picker under <see cref="Category"/> — so designers pick it (never type an FQN).
///
/// <para>
/// <b>Editor-only discovery metadata (architect Q#12).</b> The compiler never reads this attribute — it
/// resolves the call from the baked <c>TargetTypeId</c>/<c>MethodName</c> via the Roslyn semantic model,
/// exactly as for any FunctionCall. That is why this sidesteps the netstandard2.0-analyzer "can't load
/// game assemblies" limit. Trailing <c>Entity self</c> / <c>ISimulationView view</c> params keep their
/// existing recognized-and-hidden behavior.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BlueprintCallableAttribute : Attribute
{
    /// <summary>Mandatory picker group (curation knob), e.g. "Vector", "Targeting", "Math". Sub-groups via '/'.</summary>
    public string Category { get; }

    /// <summary>Optional display label in the picker; defaults to the method name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Whether the placed FunctionCall is pure (no exec pins). Defaults to <see langword="true"/> —
    /// curated helpers are value-returning pure functions. Set false for an exec-flow helper.
    /// </summary>
    public bool IsPure { get; set; } = true;

    public BlueprintCallableAttribute(string category)
    {
        Category = category;
    }
}
