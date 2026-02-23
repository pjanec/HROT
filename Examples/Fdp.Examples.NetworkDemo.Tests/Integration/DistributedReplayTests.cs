using System;
using System.Threading.Tasks;
using Xunit;
using Fdp.Examples.NetworkDemo;
using System.IO;
using Fdp.Examples.NetworkDemo.Components;
using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Network;
using ModuleHost.Core.Abstractions;


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
                     await appA.InitializeAsync(100, false, recA);
                     await appB.InitializeAsync(200, false, recB);
                     
                     for(int i=0; i<100; i++)
                     {
                         MoveLocalEntity(appA, new Vector3(0.5f, 0, 0)); 
                         MoveLocalEntity(appB, new Vector3(0, 0.5f, 0)); 
                         
                         appA.Update(0.1f);
                         appB.Update(0.1f);
                         
                         await Task.Delay(1);
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
                     await replayA.InitializeAsync(100, true, recA);
                     await replayB.InitializeAsync(200, true, recB);
                     
                     for(int i=0; i<150; i++) 
                     {
                         replayA.Update(0.1f);
                         replayB.Update(0.1f);
                         await Task.Delay(1);
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
             var q = app.World.Query()
                .With<SimTransform>()
                .With<NetworkAuthority>()
                .Build();
             
             var cmd = ((ISimulationView)app.World).GetCommandBuffer();
             foreach(var e in q)
             {
                 var tf = app.World.GetComponentRO<SimTransform>(e);
                 cmd.SetComponent(e, new SimTransform { Position = tf.Position + delta, Rotation = tf.Rotation });
             }
             ((EntityCommandBuffer)cmd).Playback(app.World);
        }
        
        private void VerifyMoved(NetworkDemoApp app, bool isRemote)
        {
             var q = app.World.Query().With<SimTransform>().With<NetworkIdentity>().Build();
             bool foundMoved = false;
             foreach(var e in q)
             {
                 var tf = app.World.GetComponentRO<SimTransform>(e);
                 if (tf.Position.Length() > 10.0f) 
                 {
                     foundMoved = true;
                     break;
                 }
             }
             Assert.True(foundMoved);
        }
    }
}
