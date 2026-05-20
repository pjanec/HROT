// Placeholder — BlueprintRegistrarAttribute implemented in M4.
namespace Fdp.Toolkit.Blueprints.Attributes;

/// <summary>Marks a static class as a Blueprint registrar invoked after assembly load.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class BlueprintRegistrarAttribute : Attribute { }
