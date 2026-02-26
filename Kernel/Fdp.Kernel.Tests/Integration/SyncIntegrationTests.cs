using Xunit;
using Fdp.Kernel;
using System.Linq;

namespace Fdp.Tests.Integration
{
    public class SyncIntegrationTests
    {
        [ComponentId(237)]
        struct Position { public float X, Y; }
        [ComponentId(238)]
        struct Velocity { public float X, Y; }
        [ComponentId(239)]
        record Identity(string Callsign);

        [Fact]
         public void FullSystemSync_GDB_Scenario()
         {
             // Setup: Create live world with entities
             using var live = new EntityRepository();
             using var replica = new EntityRepository();
             
             live.RegisterComponent<Position>();
             live.RegisterComponent<Velocity>();
             live.RegisterManagedComponent<Identity>();
             
             replica.RegisterComponent<Position>();
             replica.RegisterComponent<Velocity>();
             replica.RegisterManagedComponent<Identity>();
             
             // Create 1000 entities in live
             for (int i = 0; i < 1000; i++)
             {
                 var e = live.CreateEntity();
                 live.AddComponent(e, new Position { X = i, Y = i * 2 });
                 live.AddComponent(e, new Velocity { X = 1, Y = 1 });
                 live.AddComponent(e, new Identity($"Unit_{i}"));
             }
             
             // Execute: GDB sync (full, no mask)
             replica.SyncFrom(live);
             
             // Verify: All data copied correctly
             for (int i = 0; i < 1000; i++)
             {
                 var liveEntity = new Entity(i, 1);
                 var replicaEntity = new Entity(i, 1); 
                 
                 Assert.True(replica.IsAlive(replicaEntity));
                 
                 var livePos = live.GetComponentRO<Position>(liveEntity);
                 var replicaPos = replica.GetComponentRO<Position>(replicaEntity);
                 Assert.Equal(livePos.X, replicaPos.X);
                 Assert.Equal(livePos.Y, replicaPos.Y);
                 
                 var liveIdentity = live.GetManagedComponentRO<Identity>(liveEntity);
                 var replicaIdentity = replica.GetManagedComponentRO<Identity>(replicaEntity);
                 Assert.Same(liveIdentity, replicaIdentity);  // Shallow copy!
             }
             
             // Verify: Global version matches
             Assert.Equal(live.GlobalVersion, replica.GlobalVersion);
         }
         
         [Fact]
         public void FilteredSync_SoD_Scenario()
         {
             // Similar test but with mask filtering
             using var live = new EntityRepository();
             using var snapshot = new EntityRepository(); // SoD
             
             live.RegisterComponent<Position>();
             live.RegisterComponent<Velocity>();
             
             snapshot.RegisterComponent<Position>();
             snapshot.RegisterComponent<Velocity>();
             
             var entity = live.CreateEntity();
             live.AddComponent(entity, new Position { X=10 });
             live.AddComponent(entity, new Velocity { X=5 });
             
             var mask = new BitMask256();
             mask.SetBit(ComponentType<Position>.ID);
             
             snapshot.SyncFrom(live, mask);
             
             // Verify: Only Position copied, Velocity NOT copied
             // Reconstruct entity ID
             var snapEntity = new Entity(entity.Index, entity.Generation);
             
             Assert.True(snapshot.HasComponent<Position>(snapEntity));
             Assert.False(snapshot.HasComponent<Velocity>(snapEntity));
             
             // Check data
             Assert.Equal(10f, snapshot.GetComponentRO<Position>(snapEntity).X);
         }
    }
}
