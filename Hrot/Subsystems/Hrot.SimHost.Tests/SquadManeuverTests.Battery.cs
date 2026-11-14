using System.Numerics;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public partial class SquadManeuverTests
    {
        /// <summary>⭐ <c>CE-7038</c> (R-2098) — a mission whose guns fire NO round (the muscle refuses every one: masked or inside the minimum range;
        /// here no round ever leaves) is refused <see cref="Hrot.AI.Behaviors.Brains.BatteryFireMissionPlay.FirstRoundSeconds"/> after it began —
        /// not left running to the 120 s overrun; a mission that fired one round runs on. ✅ Red-proof: the first-round check removed ⇒ still
        /// running at 40 s ⇒ red.</summary>
        [Fact]
        public void CE7038_ABatteryThatFiresNoRound_IsRefusedAfterTheFirstRoundWindow_NotLeftRunning()
        {
            var s = Battery(new Vector3(0f, 0f, 0f), new Vector3(0f, 40f, 0f));
            Strike(s, new Vector3(3000f, 0f, 0f), Hrot.AI.Behaviors.Orders.FireMissionControl.WhenReady);
            for (int i = 0; i < 4; i++) s.Tick();
            Assert.False(s.Finished(out _));
            s.Pause(20.0); s.Tick(); s.Tick();
            Assert.False(s.Finished(out _));                     // inside the window: still laying / waiting
            s.Pause(20.0);
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 4 && !done; i++) { s.Tick(); done = s.Finished(out result); }
            Assert.True(done, "refused after the first-round window");
            Assert.Equal(Fbt.NodeStatus.Failure, result);

            var f = Battery(new Vector3(0f, 0f, 0f), new Vector3(0f, 40f, 0f));
            Strike(f, new Vector3(3000f, 0f, 0f), Hrot.AI.Behaviors.Orders.FireMissionControl.WhenReady);
            for (int i = 0; i < 4; i++) f.Tick();
            f.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(f.Members[0]).Ammo -= 1;   // one round left a tube
            f.Pause(40.0); f.Tick(); f.Tick();
            Assert.False(f.Finished(out _));                     // firing: it runs on to its rounds or the overrun
        }
    }
}
