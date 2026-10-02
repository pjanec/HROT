using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <b>Hand-built HSM machines, declared the same way JSON-authored ones are.</b>
    /// 📄 <c>DESIGN_Behavior_Self_Registration.md</c>.
    ///
    /// <para>🔒 <b>User, <c>2026-09-27</c>:</b> *"why the idle HSM blocks it? for UI authored HSM
    /// there is also no hand written registrar so why for this one?"* — ⭐ it does not, and this file
    /// is the answer. 📐 The two shapes were always identical: the generated HSM registrar emits
    /// <c>var blob = {core}.Compile(); beh.Register(id, name, new BehaviorDefinition { HsmDefinition
    /// = blob, … })</c>, which is exactly what the hand-written registrar did inline. ⛔ The only
    /// thing missing was a generator for <c>[HsmDefinition]</c> — the one attribute of eight in the
    /// engine that had no consumer (`CE-371`).</para>
    ///
    /// <para>⭐⭐ <b>Returning the GRAPH rather than a blob is deliberate.</b>
    /// <c>HsmDefinitionGenerator</c> accepts either, but only the graph shape lets the catalog emit
    /// <c>MachineMetadata</c> beside the blob — and that metadata is what symbolicates state and
    /// event names in the flight recorder, the entity inspector and <c>BrainTickSystem</c>'s log
    /// (`CE-370`). ⛔ A blob-returning method has already discarded the graph, so its metadata is
    /// null.</para>
    /// </summary>
    public static class CuratedMachines
    {
        /// <summary>
        /// ⭐ <b>Idle — a single-state machine with no transitions.</b> The default brain for an
        /// entity that has been given nothing else to do.
        /// ⚠ It declares no params, so <c>ParamsType</c> is deliberately absent: the entity gets no
        /// root params slot, which is the legitimate <c>RootParamsBytes == 0</c> case.
        /// </summary>
        [HsmDefinition("Idle", Curated = true)]
        public static StateMachineGraph BuildIdleMachine()
        {
            var builder = new HsmBuilder("Idle_HSM");
            builder.State("Idle").Initial();
            return builder.Build();
        }
    }
}
