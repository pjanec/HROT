using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.SimHost;

/// <summary>
/// ⭐⭐ ECS registration for the <b>behaviour diagnostics</b> surface — the opt-in debug flags that an
/// operator toggles, independently of whether this node runs a brain.
///
/// <para>📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.9a / §6h.</para>
///
/// <para>⭐⭐ <b>Why it is not the Brain's.</b> <c>DebugState</c> is written by <b>SimHost's own</b>
/// <c>ToggleAiTrace</c> action (<c>SimHostApp.cs:443</c>, via <c>AiTraceContextMenu.PublishToggle</c>) —
/// a node that runs no cognitive system still needs the component to record the operator's request.
/// ⇒ filing it under the cognitive set made SimHost depend on the Brain's registry for a UI affordance.</para>
///
/// <para>⛔⛔ <b>WHAT IS DELIBERATELY NOT HERE: the trace RING BUFFERS.</b>
/// <c>BTreeTraceWorkingMemory1024</c> and <c>HsmTraceWorkingMemory1024</c> stay in the Brain's registry.
/// They are written only by <c>TraceBufferLifecycleSystem</c>, which is registered by
/// <c>BehaviorDiagnosticsModule</c> on the Brain, and their only SimHost readers are EXTRACT-ONLY
/// scenario translators whose <c>CanTranslate</c> also demands <c>BehaviorState</c> — ⇒ on a node with no
/// brain they can never be populated and never dump. ⚠ §3.9a names that lost clipboard dump as an
/// accepted cost, not an oversight.</para>
/// </summary>
public static class BehaviorDiagnosticsComponentRegistry
{
    /// <summary>
    /// Registers the generic debug-flag component and the patch command that drives it.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterComponent<DebugState>();

        // ⚠ The command travels with the component: EnforceExplicitEventRegistration turns an
        //   unregistered publish into a THROW, and SimHost's toggle publishes this.
        world.RegisterManagedEvent<PatchDebugStateCommand>();
    }
}
