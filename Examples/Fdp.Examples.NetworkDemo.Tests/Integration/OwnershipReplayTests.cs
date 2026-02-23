using System;
using System.Threading.Tasks;
using Xunit;
using Fdp.Examples.NetworkDemo;
using System.IO;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Network;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Replication.Messages;


namespace Fdp.Examples.NetworkDemo.Tests.Integration
{
    public class OwnershipReplayTests
    {
        [Fact]
        public async Task CanReplay_OwnershipTransfer()
        {
            string recFile = "test_ownership.fdp";
            if (File.Exists(recFile)) File.Delete(recFile);
            if (File.Exists(recFile + ".meta")) File.Delete(recFile + ".meta");

            try
            {
                // 1. Record
                using (var app = new NetworkDemoApp())
                {
                    await app.InitializeAsync(100, false, recFile, true, false);
                    
                    Entity testEntity = default;
                    app.EnqueueAction(repo => {
                         testEntity = repo.CreateEntity();
                         var netId = new NetworkIdentity { Value = 200002 };
                         repo.AddComponent(testEntity, netId);
                         
                         var auth = new NetworkAuthority { PrimaryOwnerId = 1, LocalNodeId = 1 };
                         repo.AddComponent(testEntity, auth);
                         
                         // Add a component to track
                         repo.AddComponent(testEntity, new SimTransform());
                    });

                    for(int i=0; i<10; i++) app.Update(0.1f);
                    
                    // Transfer Ownership to Remote (2)
                    app.EnqueueAction(repo => {
                         // Simulate sending ownership transfer
                         // This should be picked up by PacketBridgeSystem/OwnershipUpdateTranslator and recorded
                         long packedKey = 0; // Master (0) + Instance (0)
                         var msg = new FDP.Toolkit.Replication.Messages.OwnershipUpdate
                         {
                             NetworkId = new NetworkIdentity { Value = 200002 },
                             PackedKey = packedKey,
                             NewOwnerNodeId = 2 // Remote
                         };
                         repo.Bus.Publish(msg);
                         
                         // Update local state to match "Live" behavior
                         var mutAuth = new NetworkAuthority { PrimaryOwnerId = 2, LocalNodeId = 1 };
                         repo.SetComponent(testEntity, mutAuth);
                    });
                    
                    for(int i=0; i<10; i++) app.Update(0.1f);
                    
                    app.Stop();
                }

                Assert.True(File.Exists(recFile), "Recording file should exist");

                // 2. Replay
                using (var app = new NetworkDemoApp())
                {
                    await app.InitializeAsync(100, true, recFile, true, false); 
                    
                    // Wait for Replay System Init
                    await Task.Delay(10);

                    bool sawOwner1 = false;
                    bool sawOwner2 = false;
                    
                    for(int i=0; i<100; i++)
                    {
                        app.Update(0.1f);
                        
                        CheckForOwner(app, 200002, ref sawOwner1, ref sawOwner2);
                        if (sawOwner1 && sawOwner2) break; // Break early
                    }
                    
                    Assert.True(sawOwner1, "Should have seen Owner 1");
                    Assert.True(sawOwner2, "Should have seen Owner 2");
                }
            }
            finally
            {
                if (File.Exists(recFile)) File.Delete(recFile);
                if (File.Exists(recFile + ".meta")) File.Delete(recFile + ".meta");
            }
        }
        
        private void CheckForOwner(NetworkDemoApp app, long netIdVal, ref bool saw1, ref bool saw2)
        {
            var q = app.World.Query().With<NetworkAuthority>().With<NetworkIdentity>().Build();
            foreach(var e in q)
            {
                var net = app.World.GetComponentRO<NetworkIdentity>(e);
                if (net.Value == netIdVal)
                {
                    var auth = app.World.GetComponentRO<NetworkAuthority>(e);
                    if (auth.PrimaryOwnerId == 1) saw1 = true;
                    if (auth.PrimaryOwnerId == 2) saw2 = true;
                }
            }
        }
    }
}
