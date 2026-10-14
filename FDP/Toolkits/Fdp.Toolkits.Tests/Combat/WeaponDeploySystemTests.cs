using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ Weapon systems WS-1 (W-C, R-2016; docs/designs/weapons/DESIGN_Weapon_Systems.md §6) — a weapon system's deploy state: placed it
    /// stands Deployed; packing takes its <c>PackSeconds</c> (Packing ⇒ Stowed), setting up its <c>DeploySeconds</c> (Deploying ⇒ Deployed),
    /// on the sim clock; only the node owning <c>dtWeaponSystemState</c> starts or advances it.
    /// </summary>
    public sealed class WeaponDeploySystemTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private readonly WeaponDeploySystem _system = new();
        private double _now;

        public WeaponDeploySystemTests()
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
            _system.Execute(_w, (float)seconds);
        }

        private Entity Weapon()
        {
            var t = new TkbTemplate("M2 HMG", 991L);
            t.AddDescriptor(new WeaponSystemDto { DeploySeconds = 6f, PackSeconds = 4f, Portable = WeaponPortability.TwoMan });
            var e = _w.CreateEntity();
            new WeaponSystemTkbTranslator().Inject(_w, e, t);
            return e;
        }

        private WeaponDeploy State(Entity e) => _w.GetComponentRO<WeaponSystemState>(e).Deploy;

        [Fact]
        public void WS1_APlacedWeapon_IsDeployed_PacksInItsPackTime_AndSetsUpInItsDeployTime()
        {
            var m2 = Weapon();
            Assert.Equal(WeaponDeploy.Deployed, State(m2));
            Assert.Equal((byte)WeaponPortability.TwoMan, _w.GetComponentRO<WeaponSystemParams>(m2).Portable);
            Assert.False(WeaponDeployment.Deploy(_w, m2));   // already deployed

            Assert.True(WeaponDeployment.Pack(_w, m2));
            Assert.Equal(WeaponDeploy.Packing, State(m2));
            Tick(3.9);
            Assert.Equal(WeaponDeploy.Packing, State(m2));
            Tick(0.2);
            Assert.Equal(WeaponDeploy.Stowed, State(m2));
            Assert.False(WeaponDeployment.Pack(_w, m2));     // already stowed

            Assert.True(WeaponDeployment.Deploy(_w, m2));
            Tick(5.9);
            Assert.Equal(WeaponDeploy.Deploying, State(m2));
            Tick(0.2);
            Assert.Equal(WeaponDeploy.Deployed, State(m2));
        }

        /// <summary>⭐ A loaded weapon keeps the state its scenario saved: the translator stamps Deployed only on a weapon that has none.</summary>
        [Fact]
        public void WS1_AStowedWeapon_StaysStowed_WhenItsTypeIsProjectedAgain()
        {
            var m2 = Weapon();
            _w.GetComponentRW<WeaponSystemState>(m2) = new WeaponSystemState { Deploy = WeaponDeploy.Stowed };
            var t = new TkbTemplate("M2 HMG", 991L);
            t.AddDescriptor(new WeaponSystemDto());
            new WeaponSystemTkbTranslator().Inject(_w, m2, t);
            Assert.Equal(WeaponDeploy.Stowed, State(m2));
        }

        /// <summary>⭐ R-136 — a node that does not own the weapon's deploy state neither starts nor advances it (its owner's sample does).</summary>
        [Fact]
        public void WS1_ANodeThatDoesNotOwnTheWeapon_NeitherStartsNorAdvancesIt()
        {
            var m2 = Weapon();
            _w.AddComponent(m2, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            Assert.False(WeaponDeployment.Pack(_w, m2));
            _w.GetComponentRW<WeaponSystemState>(m2) = new WeaponSystemState { Deploy = WeaponDeploy.Packing, Until = 0.5 };
            Tick(1.0);
            Assert.Equal(WeaponDeploy.Packing, State(m2));   // the owner's Stowed arrives by replication
        }
    }
}
