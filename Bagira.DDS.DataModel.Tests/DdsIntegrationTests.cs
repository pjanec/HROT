using Microsoft.VisualStudio.TestTools.UnitTesting;
using CycloneDDS.Runtime; // Use Runtime namespace
using System.Threading.Tasks;
using System.Linq;
using Bagira.BDC.SSTD;

namespace Bagira.DDS.DataModel.Tests
{
    [TestClass]
    public class DdsIntegrationTests
    {
        [TestMethod]
        public async Task CanPublishAndSubscribeEntityMaster()
        {
            // Arrange
            using var participant = new DdsParticipant(0); // Use DdsParticipant
            
            // In FastCycloneDds, topics are usually implicitly handled by Writer/Reader creation
            // or handled inside DdsParticipant.
            
            using var writer = new DdsWriter<EntityMaster>(participant, "EntityMaster");
            using var reader = new DdsReader<EntityMaster>(participant, "EntityMaster");
            
            var sample = new EntityMaster
            {
                EntityId = 12345,
                TkbType = 100,
                DisType = 0,
                Flags = 0
            };
            
            // Act
            // FastCycloneDDS might need slight delay for discovery
            await Task.Delay(1000); 

            writer.Write(sample);
            
            // Wait for data propagation
            await Task.Delay(1000); 
            
            var samples = reader.Take();
            
            // Assert
            Assert.IsTrue(samples.Length > 0, "Should have received at least one sample");
            Assert.AreEqual(12345, samples[0].EntityId);
            Assert.AreEqual(100, samples[0].TkbType);
        }
    }
}
