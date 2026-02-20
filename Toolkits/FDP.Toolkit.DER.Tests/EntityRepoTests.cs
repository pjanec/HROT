using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDP.Toolkit.DER;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FDP.Toolkit.DER.Tests
{
    [TestClass]
    public class EntityRepoTests
    {
        // Mock Descriptor
        public struct MockDescriptor
        {
            public int EntityId { get; set; }
            public int Version { get; set; }
            public string Data { get; set; }
        }

        public struct AnotherDescriptor
        {
            public int EntityId { get; set; }
            public int Version { get; set; }
            public int Value { get; set; }
        }

        [TestMethod]
        public void CreateAndGetEntity_Success()
        {
            var repo = new DerRepo();
            var entity = repo.CreateEntity(1, 100);

            Assert.IsNotNull(entity);
            Assert.AreEqual(1, entity.EntityId);
            Assert.AreEqual(100, entity.TkbType);

            var retrieved = repo.GetEntity(1);
            Assert.AreSame(entity, retrieved);
        }

        [TestMethod]
        public void CreateEntity_DuplicateId_Throws()
        {
            var repo = new DerRepo();
            repo.CreateEntity(1, 100);

            Assert.ThrowsException<InvalidOperationException>(() => repo.CreateEntity(1, 100));
        }

        [TestMethod]
        public void DeleteEntity_RemovesIt()
        {
            var repo = new DerRepo();
            repo.CreateEntity(1, 100);
            
            repo.DeleteEntity(1);

            Assert.IsNull(repo.GetEntity(1));
            Assert.AreEqual(0, repo.GetAllEntities().Count());
        }

        [TestMethod]
        public void EntityEvents_AreRaised()
        {
            var repo = new DerRepo();
            IDerEntity? created = null;
            IDerEntity? deleted = null;

            repo.EntityCreated += e => created = e;
            repo.EntityDeleted += e => deleted = e;

            var entity = repo.CreateEntity(1, 100);
            Assert.AreSame(entity, created);

            repo.DeleteEntity(1);
            Assert.AreSame(entity, deleted);
        }

        [TestMethod]
        public void Descriptor_SetAndGet_Works()
        {
            var repo = new DerRepo();
            var entity = repo.CreateEntity(1, 100);

            var desc = new MockDescriptor { Data = "Test" };
            entity.SetDescriptor(desc);

            Assert.IsTrue(entity.HasDescriptor<MockDescriptor>());
            
            var retrieved = entity.GetDescriptor<MockDescriptor>();
            Assert.AreEqual("Test", retrieved.Data);
        }

        [TestMethod]
        public void Descriptor_Overwrite_Works()
        {
            var repo = new DerRepo();
            var entity = repo.CreateEntity(1, 100);

            entity.SetDescriptor(new MockDescriptor { Data = "V1" });
            entity.SetDescriptor(new MockDescriptor { Data = "V2" });

            var retrieved = entity.GetDescriptor<MockDescriptor>();
            Assert.AreEqual("V2", retrieved.Data);
        }

        [TestMethod]
        public void Descriptor_MultipleTypes_Works()
        {
            var repo = new DerRepo();
            var entity = repo.CreateEntity(1, 100);

            entity.SetDescriptor(new MockDescriptor());
            entity.SetDescriptor(new AnotherDescriptor());

            Assert.IsTrue(entity.HasDescriptor<MockDescriptor>());
            Assert.IsTrue(entity.HasDescriptor<AnotherDescriptor>());
            Assert.AreEqual(2, entity.GetAllDescriptorTypes().Count());
        }

        [TestMethod]
        public void MultiPartDescriptor_SetAndGet_Works()
        {
            var repo = new DerRepo();
            var entity = repo.CreateEntity(1, 100);

            entity.SetDescriptor(new MockDescriptor { Data = "Part0" }, 0);
            entity.SetDescriptor(new MockDescriptor { Data = "Part1" }, 1);
            entity.SetDescriptor(new MockDescriptor { Data = "Part2" }, 2);

            Assert.IsTrue(entity.HasDescriptor<MockDescriptor>(0));
            Assert.IsTrue(entity.HasDescriptor<MockDescriptor>(1));
            Assert.IsTrue(entity.HasDescriptor<MockDescriptor>(2));

            Assert.AreEqual("Part0", entity.GetDescriptor<MockDescriptor>(0).Data);
            Assert.AreEqual("Part1", entity.GetDescriptor<MockDescriptor>(1).Data);
            Assert.AreEqual("Part2", entity.GetDescriptor<MockDescriptor>(2).Data);

            // Verify they don't overwrite each other
            Assert.AreEqual(1, entity.GetAllDescriptorTypes().Count()); // Still just one type
        }

        [TestMethod]
        public async Task Concurrency_StressTest()
        {
            var repo = new DerRepo();
            int numEntities = 1000;
            int numThreads = 10;

            // Concurrent Creation
            var createTasks = Enumerable.Range(0, numThreads).Select(threadId => Task.Run(() =>
            {
                for (int i = 0; i < numEntities; i++)
                {
                    int id = threadId * numEntities + i;
                    repo.CreateEntity(id, 100);
                }
            }));

            await Task.WhenAll(createTasks);
            
            Assert.AreEqual(numEntities * numThreads, repo.GetAllEntities().Count());

            // Concurrent Read/Write Descriptors
            var updateTasks = Enumerable.Range(0, numThreads).Select(threadId => Task.Run(() =>
            {
                for (int i = 0; i < numEntities; i++)
                {
                    int id = threadId * numEntities + i; // access own entities
                    var ent = repo.GetEntity(id);
                    if (ent != null)
                    {
                        ent.SetDescriptor(new MockDescriptor { Data = $"Thread{threadId}" });
                    }
                }
            }));

            await Task.WhenAll(updateTasks);

            // Verify
            var entity = repo.GetEntity(0);
            Assert.IsNotNull(entity);
            Assert.AreEqual("Thread0", entity.GetDescriptor<MockDescriptor>().Data);
        }
    }
}
