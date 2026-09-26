using Hrot.Blueprints.Core.Debug;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Editor.AiComposition;

/// <summary>
/// ⭐ The three objects a host needs to debug BTree and HSM behaviours: one tracer coordinator and
/// the two sessions that share it.
/// </summary>
/// <param name="Coordinator">The shared, time-controlling <see cref="AiTracerCoordinator"/>.</param>
/// <param name="BTree">The BTree debug session, already holding the coordinator.</param>
/// <param name="Hsm">The HSM debug session, already holding the coordinator.</param>
public sealed record AiDebugSessions(
    AiTracerCoordinator Coordinator,
    Hrot.BTree.Editor.Debug.BTreeDebugSession BTree,
    Hrot.Hsm.Editor.Debug.HsmDebugSession Hsm);

/// <summary>
/// ⭐⭐⭐ <b><c>CE-349</c> — ONE CONSTRUCTION OF THE AI DEBUG SESSIONS, CALLED BY BOTH HOSTS.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.24.
///
/// <para>📐 What was duplicated: three <c>new</c>s in each host, and they had DRIFTED — the editor
/// passed a coordinator, CGF passed none, so <c>BTreeDebugSession.Pause/Step/Continue</c> were live
/// on one host and silent no-ops on the other. ⚠ That drift is exactly what a shared composer makes
/// impossible to reintroduce.</para>
///
/// <para>⛔⛔ <b>It REFUSES a null controller, and that refusal is the point.</b> The defect this
/// programme keeps meeting is a capability that is built, registered and inert — <c>T4d</c> found it
/// here once already, when production constructed the bare <see cref="AiTracerCoordinator"/> and a
/// BTree tracer asking the simulation to stop did nothing at all, with no error and no log.
/// 🔒 *"A production caller that HAS a dependency must PASS it."* ⇒ a host that reaches this method
/// without a time controller fails LOUDLY at startup instead of shipping dead buttons.</para>
/// </summary>
public static class AiDebugSessionComposer
{
    /// <summary>
    /// ⭐ Builds the coordinator and both sessions over <paramref name="timeController"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// ⛔ <paramref name="timeController"/> is null — see the class remarks.
    /// </exception>
    public static AiDebugSessions Compose(IEngineDebugTimeController timeController)
    {
        if (timeController is null) throw new ArgumentNullException(nameof(timeController));

        var coordinator = new AiTracerCoordinator(timeController);
        return new AiDebugSessions(
            coordinator,
            new Hrot.BTree.Editor.Debug.BTreeDebugSession(coordinator),
            new Hrot.Hsm.Editor.Debug.HsmDebugSession(coordinator));
    }
}
