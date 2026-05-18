using System;
using System.Linq;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;

namespace Fhsm.Demo.Visual
{
    public static class MachineDefinitions
    {
        // Event IDs
        public const ushort PointSelected = 1;
        public const ushort Arrived = 2;
        public const ushort TimerExpired = 3;
        public const ushort ResourceFound = 4;
        public const ushort ResourceCollected = 5;
        public const ushort ResourcesDeposited = 6;
        public const ushort EnemyDetected = 7;
        public const ushort EnemyLost = 8;
        public const ushort UpdateEvent = 9;
        
        public static (HsmDefinitionBlob, MachineMetadata) CreatePatrolMachine()
        {
            var builder = new HsmBuilder("Patrol");
            
            // Register Events & Actions
            builder.Event("PointSelected", PointSelected);
            builder.Event("Arrived", Arrived);
            builder.Event("TimerExpired", TimerExpired);
            
            builder.RegisterAction("FindPatrolPoint");
            builder.RegisterAction("MoveToTarget");
            
            // Simple flat states
            var selecting = builder.State("SelectingPoint")
                .OnEntry("FindPatrolPoint");
                
            var moving = builder.State("Moving")
                .Activity("MoveToTarget");
                
            var waiting = builder.State("Waiting");
            
            // Transitions
            selecting.On(PointSelected).GoTo(moving);
            moving.On(Arrived).GoTo(waiting);
            waiting.On(TimerExpired).GoTo(selecting);
            
            // Initial state
            selecting.Initial();
            
            // Compile
            return CompileAndEmit(builder);
        }
        
        public static (HsmDefinitionBlob, MachineMetadata) CreateGatherMachine()
        {
            var builder = new HsmBuilder("Gather");
            
            // Register Events & Actions
            builder.Event("ResourceFound", ResourceFound);
            builder.Event("Arrived", Arrived);
            builder.Event("ResourceCollected", ResourceCollected);
            builder.Event("ResourcesDeposited", ResourcesDeposited);
            
            builder.RegisterAction("FindResource");
            builder.RegisterAction("MoveToResource");
            builder.RegisterAction("Gather");
            builder.RegisterAction("MoveToBase");
            builder.RegisterAction("DepositResources");
            
            // States
            var searching = builder.State("Searching")
                .OnEntry("FindResource");
                
            var movingToResource = builder.State("MovingToResource")
                .Activity("MoveToResource");
                
            var harvesting = builder.State("Harvesting")
                .OnEntry("Gather");
                
            var movingToBase = builder.State("MovingToBase")
                .Activity("MoveToBase");
                
            var depositing = builder.State("Depositing")
                .OnEntry("DepositResources");
            
            // Transitions
            searching.On(ResourceFound).GoTo(movingToResource);
            movingToResource.On(Arrived).GoTo(harvesting);
            harvesting.On(ResourceCollected).GoTo(movingToBase);
            movingToBase.On(Arrived).GoTo(depositing);
            depositing.On(ResourcesDeposited).GoTo(searching);
            
            searching.Initial();
            
            return CompileAndEmit(builder);
        }
        
        public static (HsmDefinitionBlob, MachineMetadata) CreateCombatMachine()
        {
            var builder = new HsmBuilder("Combat");
            
            // Register Events & Actions
            builder.Event("Arrived", Arrived);
            builder.Event("UpdateEvent", UpdateEvent);
            builder.Event("EnemyDetected", EnemyDetected);
            builder.Event("EnemyLost", EnemyLost);
            
            builder.RegisterAction("FindRandomPoint");
            builder.RegisterAction("MoveToTarget");
            builder.RegisterAction("ScanForEnemy");
            builder.RegisterAction("ChaseEnemy");
            builder.RegisterAction("Attack");
            
            // Simple combat states (flat for v1)
            var wandering = builder.State("Wandering")
                .OnEntry("FindRandomPoint")
                .Activity("MoveToTarget");
                
            var scanning = builder.State("Scanning")
                .Activity("ScanForEnemy");
            
            var chasing = builder.State("Chasing")
                .Activity("ChaseEnemy");
                
            var attacking = builder.State("Attacking")
                .OnEntry("Attack");
            
            // Transitions
            wandering.On(Arrived).GoTo(scanning);
            scanning.On(UpdateEvent).GoTo(wandering);
            
            // Enemy detected triggers
            wandering.On(EnemyDetected).GoTo(chasing);
            scanning.On(EnemyDetected).GoTo(chasing);
            
            // Engaging transitions
            chasing.On(Arrived).GoTo(attacking);
            attacking.On(UpdateEvent).GoTo(chasing);
            
            // Enemy lost
            chasing.On(EnemyLost).GoTo(wandering);
            attacking.On(EnemyLost).GoTo(wandering);
            
            // Initial state
            wandering.Initial();
            
            return CompileAndEmit(builder);
        }
        
        private static (HsmDefinitionBlob, MachineMetadata) CompileAndEmit(HsmBuilder builder)
        {
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            
            var errors = HsmGraphValidator.Validate(graph);
            if (errors.Count > 0)
            {
                throw new Exception($"Machine validation failed: {string.Join(", ", errors.Select(e => e.Message))}");
            }
            
            // Extract Metadata
            var metadata = new MachineMetadata();
            
            // States
            foreach(var state in graph.States.Values)
            {
                metadata.StateNames[state.FlatIndex] = state.Name;
            }
            
            // Events
            foreach(var kvp in graph.EventNameToId)
            {
                metadata.EventNames[kvp.Value] = kvp.Key;
            }
            
            // Actions (need to compute hash)
            foreach(var actionName in graph.RegisteredActions)
            {
                metadata.ActionNames[ComputeHash(actionName)] = actionName;
            }
            
            var flattened = HsmFlattener.Flatten(graph);
            return (HsmEmitter.Emit(flattened), metadata);
        }
        
        private static ushort ComputeHash(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (ushort)(hash & 0xFFFF);
        }
    }
}
