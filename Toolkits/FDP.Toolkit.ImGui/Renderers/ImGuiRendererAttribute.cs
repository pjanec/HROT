namespace FDP.Toolkit.ImGui.Renderers;

/// <summary>
/// Marks a class as a custom ImGui renderer that is auto-discovered by
/// <see cref="ImGuiRendererRegistry"/> via reflection at startup.
///
/// <para>Decorate a class implementing <see cref="IImGuiRenderer"/> with this attribute.
/// Multiple attributes on the same class allow one renderer to handle several types.</para>
///
/// <para>The class must have a public parameterless constructor.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ImGuiRendererAttribute : Attribute
{
    /// <summary>The C# type this renderer handles.</summary>
    public Type TargetType { get; }

    /// <summary>
    /// Optional context constraint. When non-<c>null</c>, this renderer only applies when
    /// rendering values that live inside the given ECS component type or outer object type.
    /// <c>null</c> means globally applicable for all occurrences of <see cref="TargetType"/>.
    /// </summary>
    public Type? OnlyInsideType { get; }

    /// <param name="targetType">The C# type this renderer handles.</param>
    /// <param name="onlyInsideType">
    /// Optional context constraint type (e.g. an ECS component type).
    /// Pass <c>null</c> (default) for globally applicable renderers.
    /// </param>
    public ImGuiRendererAttribute(Type targetType, Type? onlyInsideType = null)
    {
        TargetType     = targetType ?? throw new ArgumentNullException(nameof(targetType));
        OnlyInsideType = onlyInsideType;
    }
}
