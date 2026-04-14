using System;
using System.Threading.Tasks;
using Xunit;
using Fdp.Examples.NetworkDemo;
using System.IO;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using Fdp.ModuleHost.Core.Network;
using Fdp.ModuleHost.Core.Abstractions;

using System.Collections.Generic; // Added

namespace Fdp.Examples.NetworkDemo.Tests.Integration
{
    public class ManagedReplayTests
    {
        [Fact]
        public async Task CanRecordAndReplay_ManagedComponent()
        {
            string recFile = "test_managed_chat.fdp";
            if (File.Exists(recFile)) File.Delete(recFile);
            if (File.Exists(recFile + ".meta")) File.Delete(recFile + ".meta");

            try
            {
                // 1. Record
                using (var app = new NetworkDemoApp())
                {
                    await app.InitializeAsync(100, false, recFile, true, false, testMode: true);
                    
                    Entity chatEntity = default;
                    app.EnqueueAction(repo => {
                         // IDs in System Range (0-65535) are NOT recorded by default logic in NetworkDemoApp
                         // "recorderSys.SetMinRecordableId(FdpConfig.SYSTEM_ID_RANGE);"
                         // But CreateEntity() returns 1, 2, 3...
                         // Wait, NetworkDemoApp reserves range: "World.ReserveIdRange(FdpConfig.SYSTEM_ID_RANGE);"
                         // So CreateEntity() returns > 65536.
                         
                         chatEntity = repo.CreateEntity();
                         var netId = new NetworkIdentity { Value = 100001 };
                         repo.AddComponent(chatEntity, netId);
                         
                         // We must add NetworkAuthority so ReplayBridge knows we own it?
                         var auth = new NetworkAuthority { PrimaryOwnerId = 100, LocalNodeId = 100 };
                         repo.AddComponent(chatEntity, auth);
                         
                         var chat = new SquadChat();
                         chat.EntityId = netId.Value;
                         chat.SenderName = "Alpha";
                         chat.Message = "Initial";
                         
                         repo.SetManagedComponent(chatEntity, chat);
                    });

                    for(int i=0; i<5; i++) app.Update(0.1f);
                    
                    app.EnqueueAction(repo => {
                         var chat = ((ISimulationView)repo).GetManagedComponentRO<SquadChat>(chatEntity);
                         var mutable = new SquadChat { EntityId = chat.EntityId, SenderName = chat.SenderName, Message = "Replaced Message" };
                         repo.SetManagedComponent(chatEntity, mutable);
                    });
                    
                    // Record longer to ensure frame capture
                    for(int i=0; i<20; i++) app.Update(0.1f);
                    
                    app.Stop();
                }

                Assert.True(File.Exists(recFile), "Recording file should exist");

                // 2. Replay
                using (var app = new NetworkDemoApp())
                {
                    await app.InitializeAsync(100, true, recFile, true, false); 
                    
                    // Allow FS/Replay init
                    await Task.Delay(10);

                    SquadChat chatState = null;
                    
                    // Run enough frames to hit the update
                    for(int i=0; i<100; i++)
                    {
                        app.Update(0.1f);
                        
                        chatState = CheckForUpdatedChat(app);
                        if (chatState != null && chatState.Message == "Replaced Message") break;
                    }
                    
                    Assert.NotNull(chatState);
                    Assert.Equal("Replaced Message", chatState.Message);
                }
            }
            finally
            {
                if (File.Exists(recFile)) File.Delete(recFile);
                if (File.Exists(recFile + ".meta")) File.Delete(recFile + ".meta");
            }
        }
        
        private SquadChat CheckForUpdatedChat(NetworkDemoApp app)
        {
            var q = app.World.Query().With<NetworkIdentity>().WithManaged<SquadChat>().Build();
            foreach(var e in q)
            {
                var netId = app.World.GetComponent<NetworkIdentity>(e);
                if (netId.Value == 100001)
                {
                    var c = ((ISimulationView)app.World).GetManagedComponentRO<SquadChat>(e);
                    if (c.Message == "Replaced Message")
                    {
                        return c;
                    }
                }
            }
            return null;
        }
    }
}
