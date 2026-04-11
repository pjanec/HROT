using System;
using Xunit;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Messages;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;

namespace FDP.Toolkit.Replication.Tests
{
    public class IdAllocationTests
    {
        [Fact]
        public void MonitorSystem_PublishesRequest_WhenLowWaterMarkTriggers()
        {
            using var repo = new EntityRepository();
            var monitor = new IdAllocationMonitorSystem();
            monitor.Create(repo);
            
            // Setup Manager
            var manager = new BlockIdManager(10);
            repo.SetSingletonManaged(manager);
            
            // Run system once to attach listeners
            monitor.Run();
            
            // Trigger Low Water Mark
            try 
            {
               manager.AllocateId();
            }
            catch (InvalidOperationException) 
            { 
            }
            
            // Run System
            monitor.Run();
            
            repo.Bus.SwapBuffers(); 
            
            // Use ConsumeManaged for the class-based event
            var requests = repo.Bus.ConsumeManaged<IdBlockRequest>();
            Assert.NotEmpty(requests);
            Assert.NotNull(requests[0].ClientId);
        }
        
        [Fact]
        public void MonitorSystem_ProcessesResponse_AndAddsBlock()
        {
            using var repo = new EntityRepository();
            var monitor = new IdAllocationMonitorSystem();
            monitor.Create(repo);
            
            // Setup Manager
            var manager = new BlockIdManager();
            repo.SetSingletonManaged(manager);
            
            // Run system once to attach listeners
            monitor.Run();
            
            // 1. Trigger Request to find ClientId
            try { manager.AllocateId(); } catch {}
            monitor.Run();
            repo.Bus.SwapBuffers();
            var requests = repo.Bus.ConsumeManaged<IdBlockRequest>();
            var clientId = requests[0].ClientId;
            
            // 2. Send Response
            var resp = new IdBlockResponse
            {
                ClientId = clientId,
                StartId = 1000,
                Count = 50
            };
            repo.Bus.PublishManaged(resp);
            repo.Bus.SwapBuffers(); // Make response visible
            
            // 3. Run System to process response
            monitor.Run();
            
            // 4. Verify Block Added
            Assert.Equal(50, manager.AvailableCount); 
        }
    }
}
