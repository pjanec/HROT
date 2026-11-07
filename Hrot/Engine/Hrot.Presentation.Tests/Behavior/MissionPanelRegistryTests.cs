using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Presentation.Tests.Behavior
{
    /// <summary>
    /// ⛔ <b>P-7 7d (CE-4303): the two claims the Orders tab does NOT carry yet — this is why <see cref="MissionPanel"/> still exists.</b> Every other claim of this file was re-homed:
    /// the task's form from the host's lookup ⇒ <c>TheOrdersTabEditsTests.P7b_AnOrdersFieldsAreItsDto…</c> / <c>P7d_ATasksFormComesFromTheHostsLookup…</c>; consume-once / location /
    /// no-frame-service ⇒ <c>TheMapPickBrokerConsumesAPickOnceTests</c>; "MissionPanel is in Hrot.Presentation" ⇒ dropped (meaningless once retired).
    /// <list type="bullet">
    ///   <item><b>P4</b> — the panel's pick context is the shared broker, scoped per task. The Orders tab draws its forms through the host's <c>Pick</c> UNSCOPED
    ///     (<c>OrdersTab.DrawFields</c>: <c>BehaviorParamsForm.DrawFields(session, _pick?.Invoke(), "fields")</c>), so two tasks with an entity field share one pick key
    ///     (BehaviorParamsForm.cs:68: "one broker holds one pending pick"). The panel scoped each task: <c>MissionPanel.cs:518</c>.</item>
    ///   <item><b>CE-1059</b> — the searchable entity LIST reaches a task's form. The hosts' Details pick brokers carry no <c>EntityList</c>:
    ///     <c>EditorSubsystem.cs</c> <c>_formsPick ??= new MapPickBroker(() =&gt; _mapPickAdapter)</c> and CGF's <c>cgfPickBroker</c> (<c>CgfSubsystem.cs</c>) — only the panels got it.</item>
    /// </list>
    /// Delete this file and <see cref="MissionPanel"/> when the Orders tab scopes its task forms and the host brokers get the list.
    /// </summary>
    public sealed class MissionPanelRegistryTests
    {
        /// <summary>
        /// ⭐⭐ <c>P4</c> — the mission panel draws its behaviour parameters against THE shared pick broker, not a pick
        /// state machine of its own. 🔴 It used to implement <c>IPickInteractionContext</c> with a private copy of the
        /// broker's logic (task+property keys, <c>long</c> results); both were deleted.
        /// </summary>
        [Fact]
        public void P4_MissionPanel_PicksThroughTheSharedBroker()
        {
            Assert.IsType<Hrot.Presentation.Facades.MapPickBroker>(new MissionPanel().Picks);
        }

        /// <summary>⭐ <c>CE-1059</c> — a task's <c>EntityRef</c> param can be chosen from the searchable entity LIST as well as on the
        /// map: a host that passes <see cref="MissionPanel.EntityList"/> gives the panel's broker the List… capability, and the choice
        /// lands at the task's own scoped path, like a map pick. Without it the panel stays map-only.</summary>
        [Fact]
        public void CE1059_TheEntityList_ReachesTheTasksParamsForm_AtTheTasksScope()
        {
            Assert.False(new MissionPanel().Picks.CanListEntities);

            string[]? asked = null;
            var panel = new MissionPanel { EntityList = (presets, chosen) => { asked = presets; chosen(42); return true; } };
            var task0 = new Fdp.Presentation.Editing.ScopedMapPickContext(panel.Picks, MissionPanel.PickScope(0));
            Assert.True(task0.CanListEntities);

            task0.RequestEntityListPick("$.TargetNetworkId", new[] { "units_ground" });
            Assert.Equal(new[] { "units_ground" }, asked);
            Assert.False(new Fdp.Presentation.Editing.ScopedMapPickContext(panel.Picks, MissionPanel.PickScope(1))
                .TryConsumeEntityPick("$.TargetNetworkId", out _));                         // another task's field: not its pick
            Assert.True(task0.TryConsumeEntityPick("$.TargetNetworkId", out var picked));
            Assert.Equal(42L, picked.NetworkId);
        }
    }
}
