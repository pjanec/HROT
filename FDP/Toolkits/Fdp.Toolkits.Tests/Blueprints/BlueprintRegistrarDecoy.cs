namespace Fdp.Toolkit.Blueprints.Tests.Decoy
{
    /// <summary>CE-4109 — an attribute with the registrar attribute's NAME but not its type.</summary>
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class BlueprintRegistrarAttribute : System.Attribute { }

    /// <summary>CE-4109 — carries the decoy attribute and a Register entry point; must never be discovered.</summary>
    [BlueprintRegistrar]
    public static class DecoyRegistrar
    {
        public static void Register(Fdp.Toolkit.Blueprints.BlueprintRegistryStaging staging) { }
    }
}
