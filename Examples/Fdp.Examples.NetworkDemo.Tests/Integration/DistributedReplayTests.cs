using System;
using System.Threading.Tasks;
using Xunit;
using Fdp.Examples.NetworkDemo;
using System.IO;
using Fdp.Examples.NetworkDemo.Components;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Network;
using Fdp.ModuleHost.Abstractions;


namespace Fdp.Examples.NetworkDemo.Tests.Integration
{
    public class DistributedReplayTests
    {
        [Fact]
        public async Task FullScenario_TwoNodes_RecordAndReplay()
        {
             string recA = "test_node_100.fdp";
             string recB = "test_node_200.fdp";
             string ext = ".meta";
             
             Cleanup(recA, recB);

             try 
             {
                 using (var appA = new NetworkDemoApp())
                 using (var appB = new NetworkDemoApp())
                 {
                     await appA.InitializeAsync(100, false, recA, enableNetwork: false, testMode: true);
                     await appB.InitializeAsync(200, false, recB, enableNetwork: false, testMode: true);
                     
                     for(int i=0; i<40; i++)
                     {
                         MoveLocalEntity(appA, new Vector3(0.5f, 0, 0)); 
                         MoveLocalEntity(appB, new Vector3(0, 0.5f, 0)); 
                         
                         appA.Update(0.1f);
                         appB.Update(0.1f);
                     }
                     
                     appA.Stop();
                     appB.Stop();
                 }
                 
                 Assert.True(File.Exists(recA));
                 Assert.True(File.Exists(recB));
                 Assert.True(File.Exists(recA+ext));
                 
                 using (var replayA = new NetworkDemoApp())
                 using (var replayB = new NetworkDemoApp())
                 {
                     await replayA.InitializeAsync(100, true, recA, enableNetwork: false, testMode: true);
                     await replayB.InitializeAsync(200, true, recB, enableNetwork: false, testMode: true);
                     
                     for(int i=0; i<150; i++) 
                     {
                         replayA.Update(0.1f);
                         replayB.Update(0.1f);
                         
                         // Check early if both moved enough
                         if (CheckMoved(replayA) && CheckMoved(replayB)) break;
                     }
                     
                     VerifyMoved(replayA, false); 
                     VerifyMoved(replayB, true);
                 }
             }
             finally
             {
                 Cleanup(recA, recB);
             }
        }

        private bool CheckMoved(NetworkDemoApp app)
        {
             var q = app.World.Query().With<SimTransform>().With<NetworkIdentity>().WithLifecycle(EntityLifecycle.All).Build();
             foreach(var e in q)
             {
                 var tf = app.World.GetComponentRO<SimTransform>(e);
                 if (tf.Position.Length() > 10.0f) return true;
             }
             return false;
        }
        
        private void Cleanup(string a, string b)
        {
             string ext = ".meta";
             if (File.Exists(a)) File.Delete(a);
             if (File.Exists(b)) File.Delete(b);
             if (File.Exists(a+ext)) File.Delete(a+ext);
             if (File.Exists(b+ext)) File.Delete(b+ext);
        }
        
        private void MoveLocalEntity(NetworkDemoApp app, Vector3 delta)
        {
             // Include all lifecycle states so entities in Constructing (waiting for peer ACKs)
             // are also moved.  With reliableInitTimeoutFrames=-1, entities stay in Constructing
             // for up to 300 frames, so using the default Active filter would mean nothing moves.
             var q = app.World.Query()
                .With<SimTransform>()
                .With<NetworkAuthority>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();
             
             // Directly mutate SimTransform on the live world.
             // This is safe because PhysicsSystem skips its ECB write when velocity==0,
             // so there is no deferred ECB that would overwrite this direct write at the
             // next frame's BeforeSync flush.
             foreach(var e in q)
             {
                 ref readonly var tf = ref app.World.GetComponentRO<SimTransform>(e);
                 app.World.SetComponent(e, new SimTransform { Position = tf.Position + delta, Rotation = tf.Rotation });
             }
        }
        
        private void VerifyMoved(NetworkDemoApp app, bool isRemote)
        {
             var q = app.World.Query().With<SimTransform>().With<NetworkIdentity>().WithLifecycle(EntityLifecycle.All).Build();
             bool foundMoved = false;
             int entityCount = 0;
             float maxLength = 0f;
             foreach(var e in q)
             {
                 var tf = app.World.GetComponentRO<SimTransform>(e);
                 float len = tf.Position.Length();
                 if (len > maxLength) maxLength = len;
                 entityCount++;
                     if (tf.Position.Length() > 10.0f) 
                 {
                     foundMoved = true;
                     break;
                 }
             }
             Assert.True(foundMoved, $"Expected position.Length() > 10 but maxLen={maxLength} over {entityCount} entities (isRemote={isRemote})");
        }
    }
}
