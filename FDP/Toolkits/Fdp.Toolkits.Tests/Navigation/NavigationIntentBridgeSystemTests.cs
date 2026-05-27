using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// PACK-I003 SC3 — NavigationIntentBridgeSystem still translates NavigationIntent → NavState
    /// correctly after the removal of legacy Cmd* movement events from VehicleCommandSystem.
    /// </summary>
    public class NavigationIntentBridgeSystemTests
    {
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavigationIntent>();
            repo.RegisterComponent<NavState>();
            return repo;
        }

        /// <summary>
        /// DirectPoint intent is mapped to KinematicsMode.Direct with FinalDestination,
        /// TargetSpeed and ArrivalRadius propagated.
        /// </summary>
        [Fact]
        public void DirectPoint_Intent_MapsToDirectKinematics()
        {
            var repo = CreateWorld();
            var system = new NavigationIntentBridgeSystem();

            var entity = repo.CreateEntity();
            var dest = new Vector2(100f, 200f);
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode             = NavigationMode.DirectPoint,
                FinalDestination = dest,
                TargetSpeed      = 10f,
                ArrivalRadius    = 3.0f,
                IntentId         = 1u,
            });
            repo.AddComponent(entity, new NavState());

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);

            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.Direct, nav.Mode);
            Assert.Equal(dest, nav.FinalDestination);
            Assert.Equal(10f, nav.TargetSpeed);
            Assert.Equal(3.0f, nav.ArrivalRadius);

            repo.Dispose();
        }

        /// <summary>
        /// FollowRoute intent with a new TrajectoryId is mapped to CustomTrajectory mode
        /// and ProgressS is reset to 0 on a new intent.
        /// </summary>
        [Fact]
        public void FollowRoute_NewIntent_ResetsProgressS()
        {
            var repo = CreateWorld();
            var system = new NavigationIntentBridgeSystem();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode         = NavigationMode.FollowRoute,
                TrajectoryId = 7,
                IntentId     = 3u,
            });
            repo.AddComponent(entity, new NavState { ProgressS = 0.8f });

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);

            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.CustomTrajectory, nav.Mode);
            Assert.Equal(7, nav.TrajectoryId);
            Assert.Equal(0f, nav.ProgressS); // reset on new intent

            repo.Dispose();
        }

        /// <summary>
        /// None intent halts navigation — NavState.Mode = KinematicsMode.None, TargetSpeed = 0.
        /// </summary>
        [Fact]
        public void NoneIntent_HaltsNavigation_NavStateSetToNone()
        {
            var repo = CreateWorld();
            var system = new NavigationIntentBridgeSystem();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavigationIntent { Mode = NavigationMode.None });
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.Direct, TargetSpeed = 99f });

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);

            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.None, nav.Mode);
            Assert.Equal(0f, nav.TargetSpeed);

            repo.Dispose();
        }
    }
}
