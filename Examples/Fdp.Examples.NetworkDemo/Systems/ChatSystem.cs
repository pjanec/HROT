using System;
using Fdp.Kernel;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.ModuleHost_Core.Abstractions;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class ChatSystem : IEcsModuleSystem
    {
        private readonly int _localNodeId;
        private string _lastMessage = "";

        public ChatSystem(int nodeId) => _localNodeId = nodeId;

        public void Execute(ISimulationView view, float dt)
        {
            // 1. Receive (Reading the component updated by Network)
            // In a real app, you'd use a reactive system or event. 
            // For demo, we just poll a known entity.
            var query = view.Query().WithManaged<SquadChat>().Build();
            foreach (var e in query)
            {
                var chat = view.GetManagedComponentRO<SquadChat>(e);
                if (chat.Message != _lastMessage && chat.Message != "")
                {
                    Console.WriteLine($"[CHAT] {chat.SenderName}: {chat.Message}");
                    _lastMessage = chat.Message; // Simple de-dupe for console spam
                }
            }

            // 2. Send (Simulated Input)
            bool inputDetected = false;
            try 
            {
                // Protect against non-interactive environments (tests)
                if (!Console.IsInputRedirected && Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.C)
                {
                    inputDetected = true;
                }
            }
            catch
            {
                // Ignore console errors in tests
            }

            if (inputDetected)
            {
                // Find local player entity (assuming we can query by ownership or just pick one)
                var myTank = Entity.Null;
                
                // Attempt to find owned entity
                // This logic is simplified for demo.
                 var q = view.Query().WithManaged<SquadChat>().With<FDP.Toolkit.Replication.Components.NetworkAuthority>().Build();
                 foreach(var e in q)
                 {
                     ref readonly var own = ref view.GetComponentRO<FDP.Toolkit.Replication.Components.NetworkAuthority>(e);
                     if (own.PrimaryOwnerId == own.LocalNodeId)
                     {
                         myTank = e;
                         break;
                     }
                 }

                if (myTank != Entity.Null)
                {
                    // Update Component
                    // Note: Modifying managed component requires SetManagedComponent to trigger 'Dirty'
                    // But for reference types, we can just modify the property if we don't need change tracking flags.
                    // However, Network needs change detection.
                    
                    var chat = new SquadChat 
                    { 
                        EntityId = 0, // Will be patched by translator
                        SenderName = $"Node_{_localNodeId}",
                        Message = $"Hello from {_localNodeId} at {DateTime.Now.Second}s"
                    };
                    
                    // We need to preserve EntityId if we are replacing the object
                    // Or we assume translator handles it.
                    // Actually SetManagedComponent replaces the reference.
                    
                    // But wait, if we replace the object, we lose the previous state?
                    // SquadChat is data.
                    
                    // cmd is not available here in the snippet, we are writing to view?
                    // "view.GetCommandBuffer()" is needed.
                    // The snippet used `cmd.SetManagedComponent(myTank, chat);` but didn't show `var cmd = ...`
                    
                    if (view is EntityRepository repo)
                    {
                         repo.SetManagedComponent(myTank, chat); // Direct set on repo if possible
                    }
                    else
                    {
                         // Using command buffer
                         var cmd = view.GetCommandBuffer();
                         cmd.SetManagedComponent(myTank, chat);
                    }

                    Console.WriteLine("[CHAT] Sent message.");
                }
            }
        }
    }
}
