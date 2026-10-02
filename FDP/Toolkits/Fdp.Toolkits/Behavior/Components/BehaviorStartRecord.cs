using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-452</c> — what the entity's CURRENT root behaviour was started with</b>: its name and the parameter
    /// TEXT. Written by <c>BehaviorIngressSystem.Start</c> on every successful start, dropped by its clear.
    /// 📄 <c>Architect_Question_77</c> §5.12.
    ///
    /// <para>
    /// ⭐ Why it exists: a hot reload that re-lays-out a RUNNING behaviour's block restarts it through the ONE start
    /// pipeline (<c>SLICE2-DESIGN.md</c> Flaw 2 — <i>"re-publish <c>AssignBehaviorEvent</c>"</i>), and the restart needs
    /// the parameters it was assigned — nothing else keeps them once the parse has run.
    /// </para>
    /// <para>
    /// ⭐ TEXT, not a DTO: the text is layout-independent (the new parser reads it by name — a renamed or new field falls
    /// to its default) and belongs to no assembly; a DTO would be an instance of a type from the OLD, collectible ALC.
    /// ⭐ Sub-behaviours need none: a hosted child seeds from its parent's block (<c>HostedSubtree</c>), which the restart
    /// rebuilds from this.
    /// </para>
    /// <para>
    /// ⚠ <see cref="DataPolicy.Transient"/>: not in snapshots, recordings or scenarios. After a restore it is simply
    /// absent, and a later reload restarts on authored defaults — the behaviour before this existed.
    /// </para>
    /// </summary>
    [ComponentId(BehaviorApplicationComponentIds.BehaviorStartRecord)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class BehaviorStartRecord
    {
        /// <summary>The registered behaviour name — what <c>AssignBehaviorEvent.BehaviorName</c> takes.</summary>
        public required string BehaviorName { get; init; }

        /// <summary>The parameter JSON the behaviour was started with (<c>"{}"</c> when none was supplied).</summary>
        public required string JsonParams { get; init; }

        /// <summary>The <c>BehaviorState.InstanceId</c> of that start — the record is stale for any other.</summary>
        public required uint InstanceId { get; init; }
    }
}
