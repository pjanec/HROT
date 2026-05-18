using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fdp.Toolkit.DER;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Fdp.Toolkit.DER.Tests
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

        // ── GetAllRawDescriptors ───────────────────────────────────────────────────

        [TestMethod]
        public void GetAllRawDescriptors_NoDescriptors_ReturnsEmpty()
        {
            var repo   = new DerRepo();
            var entity = repo.CreateEntity(1, 100);

            var raw = entity.GetAllRawDescriptors().ToList();

            Assert.AreEqual(0, raw.Count);
        }

        [TestMethod]
        public void GetAllRawDescriptors_SingleDescriptor_ReturnsCorrectTypeAndData()
        {
            var repo   = new DerRepo();
            var entity = repo.CreateEntity(1, 100);
            entity.SetDescriptor(new MockDescriptor { Data = "Hello", EntityId = 1 });

            var raw = entity.GetAllRawDescriptors().ToList();

            Assert.AreEqual(1, raw.Count);
            Assert.AreEqual(typeof(MockDescriptor), raw[0].Type);
            Assert.AreEqual(0, raw[0].PartId);

            var data = (MockDescriptor)raw[0].Data;
            Assert.AreEqual("Hello", data.Data);
        }

        [TestMethod]
        public void GetAllRawDescriptors_MultipleTypes_ReturnsAll()
        {
            var repo   = new DerRepo();
            var entity = repo.CreateEntity(1, 100);
            entity.SetDescriptor(new MockDescriptor    { Data = "mock" });
            entity.SetDescriptor(new AnotherDescriptor { Value = 42 });

            var raw   = entity.GetAllRawDescriptors().ToList();
            var types = raw.Select(r => r.Type).ToHashSet();

            Assert.AreEqual(2, raw.Count);
            Assert.IsTrue(types.Contains(typeof(MockDescriptor)));
            Assert.IsTrue(types.Contains(typeof(AnotherDescriptor)));
        }

        [TestMethod]
        public void GetAllRawDescriptors_AfterUpdate_RefChanges()
        {
            // Each SetDescriptor call boxes the struct into a new heap object,
            // so the reference returned by GetAllRawDescriptors must differ.
            var repo   = new DerRepo();
            var entity = repo.CreateEntity(1, 100);
            entity.SetDescriptor(new MockDescriptor { Data = "v1" });

            var refBefore = entity.GetAllRawDescriptors()
                .First(r => r.Type == typeof(MockDescriptor)).Data;

            entity.SetDescriptor(new MockDescriptor { Data = "v2" });

            var refAfter = entity.GetAllRawDescriptors()
                .First(r => r.Type == typeof(MockDescriptor)).Data;

            Assert.IsFalse(ReferenceEquals(refBefore, refAfter),
                "Expected a new boxed reference after SetDescriptor.");
            Assert.AreEqual("v2", ((MockDescriptor)refAfter).Data);
        }

        [TestMethod]
        public void GetAllRawDescriptors_MultiPartDescriptors_ExposesAllParts()
        {
            var repo   = new DerRepo();
            var entity = repo.CreateEntity(1, 100);
            entity.SetDescriptor(new MockDescriptor { Data = "Part0" }, 0);
            entity.SetDescriptor(new MockDescriptor { Data = "Part1" }, 1);

            var raw = entity.GetAllRawDescriptors().ToList();

            Assert.AreEqual(2, raw.Count);
            Assert.IsTrue(raw.Any(r => r.PartId == 0), "Expected PartId=0");
            Assert.IsTrue(raw.Any(r => r.PartId == 1), "Expected PartId=1");

            var part0Data = (MockDescriptor)raw.First(r => r.PartId == 0).Data;
            var part1Data = (MockDescriptor)raw.First(r => r.PartId == 1).Data;
            Assert.AreEqual("Part0", part0Data.Data);
            Assert.AreEqual("Part1", part1Data.Data);
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
