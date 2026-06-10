using Fdp.ModuleHost;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Systems;

namespace Hrot.SimHost.Systems;

/// <summary>
/// Shared registration seam for the blueprint genesis + event-ingress runtime systems.
///
/// <para>Both <c>CgfSubsystem</c> and <c>EditorSubsystem</c> call this method so the
/// exact same pair of systems — <see cref="BlueprintMaterializationSystem"/> (consumes
/// <c>InitialBlueprintsIntent</c> on load) and <see cref="BlueprintEventIngressSystem"/>
/// (drains runtime attach/detach/replace events) — is present in every kernel that
/// runs blueprint-enabled scenarios.</para>
///
/// <para>Both systems are registered as global systems in the <see cref="SystemPhase.Input"/>
/// phase, matching the <c>[UpdateInPhase(SystemPhase.Input)]</c> attribute that each
/// declares on its class.</para>
/// </summary>
public static class BlueprintGenesisRuntimeRegistration
{
    /// <summary>
    /// Registers <see cref="BlueprintMaterializationSystem"/> and
    /// <see cref="BlueprintEventIngressSystem"/> into <paramref name="kernel"/>
    /// using <paramref name="registry"/> as the shared blueprint lookup.
    /// </summary>
    /// <param name="kernel">The module-host kernel to register the systems into.
    /// Must not have been initialized yet.</param>
    /// <param name="registry">The populated <see cref="BlueprintRegistry"/> instance
    /// shared between the two systems and the rest of the subsystem.</param>
    public static void RegisterBlueprintGenesisSystems(
        ModuleHostKernel kernel,
        BlueprintRegistry registry)
    {
        kernel.RegisterGlobalSystem(new BlueprintMaterializationSystem(registry));
        kernel.RegisterGlobalSystem(new BlueprintEventIngressSystem(registry));
    }
}
