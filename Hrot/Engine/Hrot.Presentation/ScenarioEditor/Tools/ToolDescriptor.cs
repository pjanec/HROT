using Fdp.Core;

namespace Hrot.ScenarioEditor.Tools
{
    /// <summary>
    /// <c>UXI-07</c> — how many of this tool may be active at once.
    /// 🔒 <c>Q27</c> ruling C (user, <c>2026-08-10</c>): <i>"Many tools are modal per subsystem, i.e. up to
    /// one currently active tool requiring focus … but a tool can be modeless, i.e. permanent until turned
    /// off."</i>
    /// </summary>
    public enum ToolModality
    {
        /// <summary>At most ONE per subsystem. Activating another cancels this one.</summary>
        Modal,

        /// <summary>Several coexist and toggle independently; untouched by the modal stack.</summary>
        Modeless,
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Which of the two focus arbiters a tool's gizmo lives in.</b>
    ///
    /// <para>⛔⛔ <b>This exists because the two arbiters cannot see each other, and that is the
    /// <c>UXI-07</c> defect.</b> 📐 <c>DataDrivenGizmoSystem.cs:91</c> and <c>GlobalGizmoManager.cs:66</c>
    /// each grant focus on <c>_focusedGizmo == null</c> — their OWN field. Both read the same
    /// <c>FdpEventBus</c> (handed to both by <c>MapInteractionPack.cs:92-100</c>), so two "exclusive" tools
    /// can hold focus simultaneously and BOTH act on one input. ⭐ Reproduced with an inverse-edit
    /// red-proof, <c>2026-09-09</c>.</para>
    ///
    /// <para>⭐ The controller needs to know which arbiter owns a tool so it can clear the OTHER one before
    /// arming. ⛔ It is deliberately NOT inferred from the gizmo type: the same gizmo class can be injected
    /// per-entity or registered globally, so ownership is a property of the ACTIVATION, not of the gizmo.</para>
    /// </summary>
    public enum ToolArbiter
    {
        /// <summary>Entity-scoped — <c>DataDrivenGizmoSystem.ActivateGizmo</c> (Rotate, Edit, Route).</summary>
        EntityScoped,

        /// <summary>Non-entity — <c>GlobalGizmoManager.Register</c> (Measure, pickers, placement).</summary>
        Global,

        /// <summary>Neither: the tool has no gizmo (the null modal tool, <c>Select</c>).</summary>
        None,
    }

    /// <summary>
    /// <c>UXI-07</c> — a tool's registration-time contract.
    /// 🔒 <c>Q27</c> (user): <i>"Tool handling should be defined by registration-time flags. No less
    /// flexibility than now."</i> and <i>"Tools do not necessarily need to be shown on the toolbar — this
    /// must be optional."</i>
    /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.
    /// </summary>
    /// <param name="Id">The tool's own vocabulary. ⛔ NOT a <c>GlobalActionId</c> — an action ACTIVATES a
    /// tool; a tool is not an action (<c>Q27</c> ruling D).</param>
    /// <param name="Label">Human-readable, for the toolbar and for unserviceable reports.</param>
    /// <param name="Modality">Modal tools contend for focus; modeless ones coexist.</param>
    /// <param name="Arbiter">Which focus arbiter owns this tool's gizmo — see <see cref="ToolArbiter"/>.</param>
    /// <param name="ShowOnToolbar">🔒 Optional by default, per the user ruling above.</param>
    /// <param name="ToggleOnReactivate">🔒 <c>Q27</c>: re-activating the current modal tool does NOT cancel
    /// it unless flagged. ⭐ <c>Edit</c>/<c>Route</c> set this — they toggled before this slice and must keep
    /// toggling; <c>Rotate</c> does not, because both hosts deliberately re-arm it.</param>
    public sealed record ToolDescriptor(
        string       Id,
        string       Label,
        ToolModality Modality,
        ToolArbiter  Arbiter,
        bool         ShowOnToolbar      = false,
        bool         ToggleOnReactivate = false);

    /// <summary>
    /// ⭐⭐⭐ <b>What happened when a tool was asked to activate. THREE outcomes, not two.</b>
    ///
    /// <para>⚠⚠ <b>Measured from <c>ToolActivationDrainSystem</c>, and this is why the delegate is not a
    /// <c>bool</c>:</b> its <c>Edit</c>/<c>Route</c> arms TOGGLE — <c>ToggleEntityGizmo</c> at
    /// <c>:176</c> calls <c>DeactivateGizmo</c> and returns when a gizmo is already injected on that
    /// entity. ⛔ Collapsing that into "false" would make the controller report it as unserviceable and
    /// mislead the operator; collapsing it into "true" would leave a dismissed tool on the modal stack.</para>
    ///
    /// <para>⛔ <b>And the toggle is PER-ENTITY, not per-descriptor</b> — pressing <c>Edit</c> on entity A
    /// then on entity B must move to B, not toggle off. ⇒ that decision stays inside the activation, which
    /// has the target; <see cref="ToolDescriptor.ToggleOnReactivate"/> is the coarser
    /// same-tool-pressed-twice rule and the drain's arms deliberately do NOT use it.</para>
    /// </summary>
    public enum ToolActivationOutcome
    {
        /// <summary>The tool armed and now holds the interaction.</summary>
        Armed,

        /// <summary>The tool deliberately turned ITSELF off (the toggle case). ⛔ Not an error.</summary>
        Dismissed,

        /// <summary>This host cannot service it. ⭐ The controller reports the reason (ruling 49).</summary>
        Unserviceable,
    }

    /// <summary>
    /// What a tool actually DOES when activated. ⭐ The same contract
    /// <c>ToolActivationDrainSystem.Unserviceable</c> already uses: say what happened, never fail silently.
    /// </summary>
    /// <param name="target">The entity the tool acts on, or <see cref="Entity.Null"/> for non-entity tools.</param>
    public delegate ToolActivationOutcome ToolActivation(Entity target);
}
