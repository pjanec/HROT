using System;
using System.Linq;
using Fhsm.Kernel;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;

namespace Fhsm.Examples.Console
{
    /// <summary>
    /// Simple traffic light state machine example.
    /// 
    /// States: Red -> Green -> Yellow -> Red
    /// Events: TimerExpired
    /// </summary>
    public static unsafe class TrafficLightExample
    {
        // Event IDs
        private const ushort TimerExpiredEvent = 1;
        
        // Context data
        private struct TrafficLightContext
        {
            public int TickCount;
        }
        
        // Actions
        [HsmAction(Name = "OnEnterRed")]
        public static void OnEnterRed(void* instance, void* context, HsmCommandWriter* writer)
        {
            System.Console.WriteLine("🔴 RED - Stop!");
        }
        
        [HsmAction(Name = "OnEnterGreen")]
        public static void OnEnterGreen(void* instance, void* context, HsmCommandWriter* writer)
        {
            System.Console.WriteLine("🟢 GREEN - Go!");
        }
        
        [HsmAction(Name = "OnEnterYellow")]
        public static void OnEnterYellow(void* instance, void* context, HsmCommandWriter* writer)
        {
            System.Console.WriteLine("🟡 YELLOW - Caution!");
        }
        
        [HsmAction(Name = "OnExitRed")]
        public static void OnExitRed(void* instance, void* context, HsmCommandWriter* writer)
        {
            System.Console.WriteLine("  Exiting Red state...");
        }
        
        // Activity (runs every tick while in state)
        [HsmAction(Name = "RedActivity")]
        public static void RedActivity(void* instance, void* context, HsmCommandWriter* writer)
        {
            var ctx = (TrafficLightContext*)context;
            System.Console.WriteLine($"  [Red Activity - Tick {ctx->TickCount}]");
        }
        
        public static void Run()
        {
            System.Console.WriteLine("=== Traffic Light State Machine ===\n");
            
             // Default namespace for generator is <Assembly>.Generated
            Fhsm.Examples.Console.Generated.HsmActionRegistrar.RegisterAll();
            
            // 1. Build state machine
            var builder = new HsmBuilder("TrafficLight");

            // Register Events & Actions (Required for validation)
            builder.Event("TimerExpired", TimerExpiredEvent);
            
            builder.RegisterAction("OnEnterRed")
                   .RegisterAction("OnExitRed")
                   .RegisterAction("RedActivity")
                   .RegisterAction("OnEnterGreen")
                   .RegisterAction("OnEnterYellow");
            
            // Define states
            var red = builder.State("Red")
                .OnEntry("OnEnterRed")
                .OnExit("OnExitRed")
                .Activity("RedActivity");
            
            var green = builder.State("Green")
                .OnEntry("OnEnterGreen");
            
            var yellow = builder.State("Yellow")
                .OnEntry("OnEnterYellow");
            
            // Define transitions
            red.On(TimerExpiredEvent).GoTo(green);
            green.On(TimerExpiredEvent).GoTo(yellow);
            yellow.On(TimerExpiredEvent).GoTo(red);
            
            // Set initial state
            red.Initial();
            
            // 2. Compile to blob
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            
            var errors = HsmGraphValidator.Validate(graph);
            if (errors.Count > 0)
            {
                System.Console.WriteLine($"Validation failed: {string.Join(", ", errors.Select(e => e.Message))}");
                return;
            }
            
            var flattened = HsmFlattener.Flatten(graph);
            var blob = HsmEmitter.Emit(flattened);
            
            System.Console.WriteLine($"Compiled: {blob.Header.StateCount} states, {blob.Header.TransitionCount} transitions\n");
            
            // 3. Create instance
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            System.Console.WriteLine($"Instance initialized (Tier: 64B)\n");
            
            // 4. Run simulation
            var context = new TrafficLightContext { TickCount = 0 };
            
            // Trigger to start
            HsmKernel.Trigger(ref instance);
            
            for (int i = 0; i < 10; i++)
            {
                context.TickCount = i;
                
                System.Console.WriteLine($"\n--- Tick {i} ---");
                
                // Fire timer event every 3 ticks
                if (i % 3 == 2)
                {
                    System.Console.WriteLine("⏰ Timer expired!");
                    var evt = new HsmEvent
                    {
                        EventId = TimerExpiredEvent,
                        Priority = EventPriority.Normal
                    };
                    
                    HsmEventQueue.TryEnqueue(&instance, 64, evt);
                }
                
                // Update
                HsmKernel.Update(blob, ref instance, context, 0.016f);
                
                System.Threading.Thread.Sleep(500); // Visual delay
            }
            
            System.Console.WriteLine("\n=== Simulation Complete ===");
        }
    }
}
