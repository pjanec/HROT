using System;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    public class PhaseTests
    {
        [Fact]
        public void DefaultPhase_IsInitialization()
        {
            using var repo = new EntityRepository();
            Assert.Equal(Phase.Initialization, repo.CurrentPhase);
        }

        [Fact]
        public void SetPhase_UpdatesCurrentPhase()
        {
            using var repo = new EntityRepository();
            repo.SetPhase(Phase.NetworkReceive);
            Assert.Equal(Phase.NetworkReceive, repo.CurrentPhase);
            
            repo.SetPhase(Phase.Simulation);
            Assert.Equal(Phase.Simulation, repo.CurrentPhase);
        }

        [Fact]
        public void AssertPhase_Throws_WhenPhaseDoesNotMatch()
        {
            using var repo = new EntityRepository();
            repo.SetPhase(Phase.NetworkReceive); // Init -> NetRecv
            repo.SetPhase(Phase.Simulation);     // NetRecv -> Sim
            
            Assert.Throws<WrongPhaseException>(() => repo.AssertPhase(Phase.NetworkReceive));
        }

        [Fact]
        public void AssertPhase_Succeeds_WhenPhaseMatches()
        {
            using var repo = new EntityRepository();
            repo.SetPhase(Phase.NetworkReceive); // Valid
            
            var exception = Record.Exception(() => repo.AssertPhase(Phase.NetworkReceive));
            Assert.Null(exception);
        }

        [Fact]
        public void WriteAccess_EnforcesRules()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            // Setup
            var owned = repo.CreateEntity();
            repo.AddComponent(owned, new Position());
            repo.SetAuthority<Position>(owned, true);
            
            var remote = repo.CreateEntity();
            repo.AddComponent(remote, new Position());
            repo.SetAuthority<Position>(remote, false);
            
            // 1. NetworkReceive
            repo.SetPhase(Phase.NetworkReceive);
            // Owned -> Fail
            Assert.Throws<InvalidOperationException>(() => repo.GetComponentRW<Position>(owned));
            Assert.Throws<InvalidOperationException>(() => repo.SetUnmanagedComponent(owned, new Position()));
            // Remote -> OK
            repo.GetComponentRW<Position>(remote).X = 10;
            repo.SetUnmanagedComponent(remote, new Position { X = 20 });
            
            // 2. Simulation
            repo.SetPhase(Phase.Simulation);
            // Owned -> OK
            repo.GetComponentRW<Position>(owned).X = 10;
            repo.SetUnmanagedComponent(owned, new Position { X = 20 });
            // Remote -> Fail
            Assert.Throws<InvalidOperationException>(() => repo.GetComponentRW<Position>(remote));
            Assert.Throws<InvalidOperationException>(() => repo.SetUnmanagedComponent(remote, new Position()));
            
            // 3. NetworkSend / Presentation -> All Fail
            repo.SetPhase(Phase.NetworkSend);
            Assert.Throws<InvalidOperationException>(() => repo.GetComponentRW<Position>(owned));
            
            repo.SetPhase(Phase.Presentation);
            Assert.Throws<InvalidOperationException>(() => repo.GetComponentRW<Position>(owned));
        }
        
        [Fact]
        public void RelaxedConfig_AllowsAll()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            // EXTERNALLY CONFIGURE: Use relaxed config
            repo.PhaseConfig = PhaseConfig.Relaxed;
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position());
            
            // Allow Transition Initialization -> Presentation (Normally invalid)
            repo.SetPhase(Phase.Presentation);
            Assert.Equal(Phase.Presentation, repo.CurrentPhase);
            
            // Allow Write in "Presentation" (Normally ReadOnly)
            repo.GetComponentRW<Position>(e).X = 99;
            Assert.Equal(99, repo.GetComponentRO<Position>(e).X);
        }
        
        [Fact]
        public void DefaultConfig_EnforcesStrictTransitions()
        {
            using var repo = new EntityRepository();
            
            // EXTERNALLY CONFIGURE: Explicitly set default (strict) config
            repo.PhaseConfig = PhaseConfig.Default;
            
            // Valid transition chain
            repo.SetPhase(Phase.NetworkReceive);    // Init -> NetworkReceive ✅
            repo.SetPhase(Phase.Simulation);        // NetworkReceive -> Simulation ✅
            repo.SetPhase(Phase.NetworkSend);       // Simulation -> NetworkSend ✅
            repo.SetPhase(Phase.Presentation);      // NetworkSend -> Presentation ✅
            
            // Invalid skip
            Assert.Throws<InvalidOperationException>(() => 
                repo.SetPhase(Phase.Simulation));   // Presentation -> Simulation ❌
        }
        
        [Fact]
        public void CustomConfig_AllowsSelfLoop()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            // EXTERNALLY CONFIGURE: Custom config for turn-based game
            var customConfig = new PhaseConfig();
            customConfig.ValidTransitions = new()
            {
                ["Initialization"] = new() { "Simulation" },
                ["Simulation"]     = new() { "Simulation", "Presentation" }, // Self-loop!
                ["Presentation"]   = new() { "Simulation" }
            };
            customConfig.Permissions = new()
            {
                ["Initialization"] = PhasePermission.ReadWriteAll,
                ["Simulation"]     = PhasePermission.ReadWriteAll,
                ["Presentation"]   = PhasePermission.ReadOnly
            };
            
            repo.PhaseConfig = customConfig;
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position());
            
            // Can transition to Simulation
            repo.SetPhase(Phase.Simulation);
            repo.GetComponentRW<Position>(e).X = 10;
            
            // Can stay in Simulation (self-loop)
            repo.SetPhase(Phase.Simulation);
            repo.GetComponentRW<Position>(e).X = 20;
            
            // Can go to Presentation
            repo.SetPhase(Phase.Presentation);
            Assert.Equal(20, repo.GetComponentRO<Position>(e).X);
            
            // Cannot write in Presentation
            Assert.Throws<InvalidOperationException>(() => 
                repo.GetComponentRW<Position>(e));
        }
        
        [Fact]
        public void CustomConfig_ReadWriteAllBypassesAuthority()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            // EXTERNALLY CONFIGURE: Custom config without authority checks
            var customConfig = new PhaseConfig();
            customConfig.ValidTransitions = new()
            {
                ["Initialization"] = new() { "Simulation" },
                ["Simulation"]     = new() { "Simulation" }
            };
            customConfig.Permissions = new()
            {
                ["Initialization"] = PhasePermission.ReadWriteAll,
                ["Simulation"]     = PhasePermission.ReadWriteAll  // No authority check!
            };
            
            repo.PhaseConfig = customConfig;
            
            var owned = repo.CreateEntity();
            repo.AddComponent(owned, new Position());
            repo.SetAuthority<Position>(owned, true);
            
            var remote = repo.CreateEntity();
            repo.AddComponent(remote, new Position());
            repo.SetAuthority<Position>(remote, false);
            
            repo.SetPhase(Phase.Simulation);
            
            // Can modify BOTH owned and remote (ReadWriteAll ignores authority)
            repo.GetComponentRW<Position>(owned).X = 100;   // ✅ OK
            repo.GetComponentRW<Position>(remote).X = 200;  // ✅ OK (would fail with Default config)
            
            Assert.Equal(100, repo.GetComponentRO<Position>(owned).X);
            Assert.Equal(200, repo.GetComponentRO<Position>(remote).X);
        }
        
        [Fact]
        public void PhaseConfigChange_UpdatesPermissions()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position());
            
            // Start with relaxed config
            repo.PhaseConfig = PhaseConfig.Relaxed;
            repo.SetPhase(Phase.Presentation);
            repo.GetComponentRW<Position>(e).X = 10;  // ✅ OK with relaxed
            
            // Switch to strict config
            repo.PhaseConfig = PhaseConfig.Default;
            
            // Now same operation fails
            Assert.Throws<InvalidOperationException>(() => 
                repo.GetComponentRW<Position>(e));  // ❌ ReadOnly in default config
        }
        
        [Fact]
        public void CustomPhases_MultipleSimulationPhases()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            // EXTERNALLY CONFIGURE: Multiple simulation-like phases
            var customConfig = new PhaseConfig();
            customConfig.ValidTransitions = new()
            {
                ["Init"] = new() { "PhysicsSim" },
                ["PhysicsSim"] = new() { "AISim" },
                ["AISim"] = new() { "CombatSim" },
                ["CombatSim"] = new() { "RenderWorld", "NetworkSync" },
                ["RenderWorld"] = new() { "RenderUI" },
                ["RenderUI"] = new() { "NetworkSync" },
                ["NetworkSync"] = new() { "PhysicsSim" }  // Loop back
            };
            customConfig.Permissions = new()
            {
                ["Init"] = PhasePermission.ReadWriteAll,
                ["PhysicsSim"] = PhasePermission.ReadWriteAll,
                ["AISim"] = PhasePermission.ReadWriteAll,
                ["CombatSim"] = PhasePermission.ReadWriteAll,
                ["RenderWorld"] = PhasePermission.ReadOnly,
                ["RenderUI"] = PhasePermission.ReadOnly,
                ["NetworkSync"] = PhasePermission.ReadOnly
            };
            
            repo.PhaseConfig = customConfig;
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position());
            
            // Custom phase flow with multiple simulation phases
            var physicsPhase = new Phase("PhysicsSim");
            var aiPhase = new Phase("AISim");
            var combatPhase = new Phase("CombatSim");
            var renderWorldPhase = new Phase("RenderWorld");
            var renderUIPhase = new Phase("RenderUI");
            
            repo.SetPhase(physicsPhase);
            repo.GetComponentRW<Position>(e).X = 10;  // ✅ Can write during physics sim
            
            repo.SetPhase(aiPhase);
            repo.GetComponentRW<Position>(e).X = 20;  // ✅ Can write during AI sim
            
            repo.SetPhase(combatPhase);
            repo.GetComponentRW<Position>(e).X = 30;  // ✅ Can write during combat sim
            
            repo.SetPhase(renderWorldPhase);
            Assert.Equal(30, repo.GetComponentRO<Position>(e).X);
            Assert.Throws<InvalidOperationException>(() => 
                repo.GetComponentRW<Position>(e));  // ❌ ReadOnly during render
            
            repo.SetPhase(renderUIPhase);
            Assert.Throws<InvalidOperationException>(() => 
                repo.GetComponentRW<Position>(e));  // ❌ ReadOnly during UI render
        }
    }
}
