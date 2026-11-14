using Fdp.Core;
using Fdp.Interfaces;
using Fbt;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ WS-6 (📄 docs/designs/weapons/DESIGN_Weapon_Systems.md §10) — <see cref="WeaponSystemNodes.DeployWeapon"/>, the one new weapon-system
    /// node: it starts a set-up / pack-up and WAITS for the state the real <see cref="WeaponDeploySystem"/> advances.
    /// </summary>
    public sealed class WeaponSystemNodesTests : System.IDisposable
    {
        private readonly EntityRepository _w = new();
        private readonly WeaponDeploySystem _deploy = new();
        private double _now;

        public WeaponSystemNodesTests()
        {
            _w.RegisterComponent<WeaponSystemParams>();
            _w.RegisterComponent<WeaponSystemState>();
            _w.RegisterComponent<NetworkAuthority>();
            Tick(0);
        }

        public void Dispose() => _w.Dispose();

        private void Tick(double seconds)
        {
            _now += seconds;
            _w.SetSingleton(new GlobalTime { TotalTime = _now, TimeScale = 1f });
            _deploy.Execute(_w, (float)seconds);
        }

        private Entity Weapon(WeaponDeploy start)
        {
            var t = new TkbTemplate("M2 HMG", 991L);
            t.AddDescriptor(new WeaponSystemDto { DeploySeconds = 6f, PackSeconds = 4f, Portable = WeaponPortability.TwoMan });
            var e = _w.CreateEntity();
            new WeaponSystemTkbTranslator().Inject(_w, e, t);
            _w.GetComponentRW<WeaponSystemState>(e) = new WeaponSystemState { Deploy = start };
            return e;
        }

        private NodeStatus Run(ref DeployWeaponParams p) => WeaponSystemNodes.DeployWeapon(ref p, Entity.Null, _w);

        private WeaponDeploy State(Entity e) => _w.GetComponentRO<WeaponSystemState>(e).Deploy;

        /// <summary>A stowed weapon: the node starts the set-up, is Running through its 6 s, and Success at Deployed — then at once on a
        /// deployed one. 🔴 Red-proof: return Success after starting (not waiting) and the second assertion fails.</summary>
        [Fact]
        public void WS6_DeployWeapon_SetsItUp_AndWaitsForDeployed()
        {
            var m2 = Weapon(WeaponDeploy.Stowed);
            var p = new DeployWeaponParams { Weapon = m2 };
            Assert.Equal(NodeStatus.Running, Run(ref p));
            Assert.Equal(WeaponDeploy.Deploying, State(m2));
            Tick(5.9);
            Assert.Equal(NodeStatus.Running, Run(ref p));
            Tick(0.2);
            Assert.Equal(WeaponDeploy.Deployed, State(m2));
            Assert.Equal(NodeStatus.Success, Run(ref p));
            Assert.Equal(NodeStatus.Success, Run(ref p));   // already deployed: at once
        }

        /// <summary>Packing waits for Stowed; a pack someone else started is waited on, not refused.</summary>
        [Fact]
        public void WS6_DeployWeapon_Packs_AndWaitsForStowed_EvenWhenItWasAlreadyPacking()
        {
            var m2 = Weapon(WeaponDeploy.Deployed);
            Assert.True(WeaponDeployment.Pack(_w, m2));   // someone else started it
            var p = new DeployWeaponParams { Weapon = m2, Pack = 1 };
            Assert.Equal(NodeStatus.Running, Run(ref p));
            Tick(4.1);
            Assert.Equal(NodeStatus.Success, Run(ref p));
            Assert.Equal(WeaponDeploy.Stowed, State(m2));
        }

        /// <summary>A weapon whose deploy state another node owns is not set up here (Failure, nothing written); a dead one fails too.</summary>
        [Fact]
        public void WS6_DeployWeapon_Fails_OnAWeaponItDoesNotOwn_OrOneThatIsGone()
        {
            var m2 = Weapon(WeaponDeploy.Stowed);
            _w.AddComponent(m2, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            var p = new DeployWeaponParams { Weapon = m2 };
            Assert.Equal(NodeStatus.Failure, Run(ref p));
            Assert.Equal(WeaponDeploy.Stowed, State(m2));
            var gone = new DeployWeaponParams { Weapon = Entity.Null };
            Assert.Equal(NodeStatus.Failure, Run(ref gone));
        }

        /// <summary>A set-up turned back to packing mid-way fails (it will not report a deploy that is no longer happening).</summary>
        [Fact]
        public void WS6_DeployWeapon_Fails_WhenTheChangeIsTurnedBack()
        {
            var m2 = Weapon(WeaponDeploy.Stowed);
            var p = new DeployWeaponParams { Weapon = m2 };
            Assert.Equal(NodeStatus.Running, Run(ref p));
            Tick(1.0);
            Assert.True(WeaponDeployment.Pack(_w, m2));
            Assert.Equal(NodeStatus.Failure, Run(ref p));
        }
    }
}
