using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;

namespace Hrot.SimHost;

/// <summary>
/// ⭐⭐ ECS registration for <b>embarkation</b> — the runtime state of who is riding in what.
///
/// <para>📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.9a / §6h.</para>
///
/// <para>⛔⛔ <b>Why this is its own registry rather than a line added to an existing one.</b> Embarkation
/// spans TWO roles and an editor: SimHost's <c>GenesisMaterializationSystem</c> materialises the state,
/// the Brain's <c>EmbarkExecutor</c> / <c>EjectPassengersExecutor</c> drive it, and the Editor's
/// <c>EditorCargoSystem</c> authors it. ⇒ it belongs to no single role, and it had been parked in
/// <c>CognitiveComponentRegistry</c> — the BRAIN's set — which is one of the reasons SimHost had to call
/// the Brain's registry at all.</para>
///
/// <para>⚠ <b>The nearest existing name is WRONG and was rejected deliberately:</b>
/// <c>GenesisIntentRegistry</c> holds scenario-load <i>intent DTOs</i> (<c>InitialPassengersIntent</c>) —
/// declarative initial conditions. ⛔ These are the RUNTIME components those intents materialise INTO;
/// filing them together would repeat the category error this whole split is undoing.</para>
/// </summary>
public static class EmbarkationComponentRegistry
{
    /// <summary>
    /// Registers the embarkation runtime components and their commands.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterComponent<PassengerBuffer>();
        world.RegisterComponent<IsEmbarkedTag>();

        // ⚠ The commands travel WITH the components deliberately. FdpConfig.EnforceExplicitEventRegistration
        //   makes an unregistered publish THROW, so splitting the event from the state it mutates would
        //   turn a registry omission into a runtime crash on whichever host publishes first.
        world.RegisterEvent<EmbarkEntityCommand>();
        world.RegisterEvent<DisembarkEntityCommand>();
    }
}
