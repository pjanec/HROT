// Marks a static class as a Blueprint registrar invoked by AiHotReloadCoordinator after assembly load.
namespace Fdp.Toolkit.Blueprints.Attributes;

/// <summary>Marks a static class as a Blueprint registrar invoked after assembly load.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class BlueprintRegistrarAttribute : Attribute { }
