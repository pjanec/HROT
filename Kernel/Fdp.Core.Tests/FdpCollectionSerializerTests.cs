using Xunit;
using Fdp.Kernel.FlightRecorder;
using MessagePack;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace Fdp.Tests
{
    /// <summary>
    /// Comprehensive tests for collection serialization (Dict, HashSet, Queue, Stack, Concurrent variants)
    /// Critical for Flight Recorder managed component support
    /// </summary>
    public class FdpCollectionSerializerTests
    {
        #region Test Helper Types
        
        [MessagePackObject]
        public class Player
        {
            [Key(0)]
            public int Id { get; set; }
            
            [Key(1)]
            public string? Name { get; set; }
            
            [Key(2)]
            public float Score { get; set; }
        }
        
        #endregion
        
        #region Dictionary Tests
        
        [MessagePackObject]
        public class TestDictObject
        {
            [Key(0)]
            public Dictionary<string, int>? Scores { get; set; }
        }
        
        [Fact]
        public void Serialize_DictionaryStringInt_RoundTrip()
        {
            // Arrange
            var obj = new TestDictObject
            {
                Scores = new Dictionary<string, int>
                {
                    { "Alice", 100 },
                    { "Bob", 200 },
                    { "Charlie", 300 }
                }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestDictObject>(reader);
            
            // Assert
            Assert.Equal(3, result!.Scores!.Count);
            Assert.Equal(100, result.Scores!["Alice"]);
            Assert.Equal(200, result.Scores["Bob"]);
            Assert.Equal(300, result.Scores["Charlie"]);
        }
        
        [MessagePackObject]
        public class TestDictCustomObject
        {
            [Key(0)]
            public Dictionary<int, Player> Players { get; set; } = null!;
        }
        
        [Fact]
        public void Serialize_DictionaryWithCustomValues_RoundTrip()
        {
            // Arrange
            var obj = new TestDictCustomObject
            {
                Players = new Dictionary<int, Player>
                {
                    { 1, new Player { Id = 1, Name = "Hero", Score = 99.5f } },
                    { 2, new Player { Id = 2, Name = "Villain", Score = 88.8f } },
                    { 3, null! } // Null value
                }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestDictCustomObject>(reader);
            
            // Assert
            Assert.Equal(3, result.Players.Count);
            Assert.Equal("Hero", result.Players[1].Name);
            Assert.Equal(99.5f, result.Players[1].Score, precision: 5);
            Assert.Equal("Villain", result.Players[2].Name);
            Assert.Null(result.Players[3]);
        }
        
        [MessagePackObject]
        public class TestEmptyDictObject
        {
            [Key(0)]
            public Dictionary<string, string>? Data { get; set; }
        }
        
        [Fact]
        public void Serialize_EmptyDictionary_RoundTrip()
        {
            // Arrange
            var obj = new TestEmptyDictObject
            {
                Data = new Dictionary<string, string>()
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestEmptyDictObject>(reader);
            
            // Assert
            Assert.NotNull(result.Data);
            Assert.Empty(result.Data);
        }
        
        #endregion
        
        #region HashSet Tests
        
        [MessagePackObject]
        public class TestHashSetObject
        {
            [Key(0)]
            public HashSet<string>? Tags { get; set; }
        }
        
        [Fact]
        public void Serialize_HashSet_RoundTrip()
        {
            // Arrange
            var obj = new TestHashSetObject
            {
                Tags = new HashSet<string> { "alpha", "beta", "gamma", "delta" }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestHashSetObject>(reader);
            
            // Assert
            Assert.Equal(4, result!.Tags!.Count);
            Assert.Contains("alpha", result.Tags!);
            Assert.Contains("beta", result.Tags);
            Assert.Contains("gamma", result.Tags);
            Assert.Contains("delta", result.Tags);
        }
        
        [MessagePackObject]
        public class TestHashSetCustomObject
        {
            [Key(0)]
            public HashSet<Player>? UniquePlayers { get; set; }
        }
        
        [Fact]
        public void Serialize_HashSetCustomType_RoundTrip()
        {
            // Arrange
            var obj = new TestHashSetCustomObject
            {
                UniquePlayers = new HashSet<Player>
                {
                    new Player { Id = 1, Name = "P1", Score = 10 },
                    new Player { Id = 2, Name = "P2", Score = 20 }
                }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestHashSetCustomObject>(reader);
            
            // Assert
            Assert.Equal(2, result!.UniquePlayers!.Count);
            Assert.Contains(result.UniquePlayers!, p => p!.Name == "P1");
            Assert.Contains(result.UniquePlayers, p => p!.Name == "P2");
        }
        
        #endregion
        
        #region Queue Tests
        
        [MessagePackObject]
        public class TestQueueObject
        {
            [Key(0)]
            public Queue<int>? Commands { get; set; }
        }
        
        [Fact]
        public void Serialize_Queue_PreservesOrder()
        {
            // Arrange
            var obj = new TestQueueObject
            {
                Commands = new Queue<int>(new[] { 1, 2, 3, 4, 5 })
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestQueueObject>(reader);
            
            // Assert
            Assert.Equal(5, result!.Commands!.Count);
            Assert.Equal(1, result.Commands!.Dequeue());
            Assert.Equal(2, result.Commands!.Dequeue());
            Assert.Equal(3, result.Commands!.Dequeue());
            Assert.Equal(4, result.Commands!.Dequeue());
            Assert.Equal(5, result.Commands!.Dequeue());
        }
        
        [MessagePackObject]
        public class TestQueueCustomObject
        {
            [Key(0)]
            public Queue<Player>? PlayerQueue { get; set; }
        }
        
        [Fact]
        public void Serialize_QueueCustomType_RoundTrip()
        {
            // Arrange
            var obj = new TestQueueCustomObject
            {
                PlayerQueue = new Queue<Player>(new Player?[]
                {
                    new Player { Id = 1, Name = "First", Score = 100 },
                    null, // Null in queue
                    new Player { Id = 2, Name = "Second", Score = 200 }
                }!)
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestQueueCustomObject>(reader);
            
            // Assert
            Assert.Equal(3, result!.PlayerQueue!.Count);
            var first = result.PlayerQueue.Dequeue();
            Assert.Equal("First", first!.Name);
            var nullPlayer = result.PlayerQueue.Dequeue();
            Assert.Null(nullPlayer);
            var second = result.PlayerQueue.Dequeue();
            Assert.Equal("Second", second.Name);
        }
        
        #endregion
        
        #region Stack Tests
        
        [MessagePackObject]
        public class TestStackObject
        {
            [Key(0)]
            public Stack<string>? UndoStack { get; set; }
        }
        
        [Fact]
        public void Serialize_Stack_PreservesLIFO()
        {
            // Arrange
            var obj = new TestStackObject
            {
                UndoStack = new Stack<string>(new[] { "action1", "action2", "action3" })
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestStackObject>(reader);
            
            // Assert - Stack was created with array, so LIFO order should be reversed
            Assert.Equal(3, result!.UndoStack!.Count);
            Assert.Equal("action3", result.UndoStack!.Pop());
            Assert.Equal("action2", result.UndoStack!.Pop());
            Assert.Equal("action1", result.UndoStack!.Pop());
        }
        
        #endregion
        
        #region ConcurrentDictionary Tests
        
        [MessagePackObject]
        public class TestConcurrentDictObject
        {
            [Key(0)]
            public ConcurrentDictionary<int, string>? ThreadSafeData { get; set; }
        }
        
        [Fact]
        public void Serialize_ConcurrentDictionary_RoundTrip()
        {
            // Arrange
            var obj = new TestConcurrentDictObject
            {
                ThreadSafeData = new ConcurrentDictionary<int, string>()
            };
            obj.ThreadSafeData.TryAdd(1, "One");
            obj.ThreadSafeData.TryAdd(2, "Two");
            obj.ThreadSafeData.TryAdd(3, "Three");
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestConcurrentDictObject>(reader);
            
            // Assert
            Assert.Equal(3, result!.ThreadSafeData!.Count);
            Assert.Equal("One", result.ThreadSafeData![1]);
            Assert.Equal("Two", result.ThreadSafeData![2]);
            Assert.Equal("Three", result.ThreadSafeData![3]);
        }
        
        #endregion
        
        #region ConcurrentBag Tests
        
        [MessagePackObject]
        public class TestConcurrentBagObject
        {
            [Key(0)]
            public ConcurrentBag<int>? Numbers { get; set; }
        }
        
        [Fact]
        public void Serialize_ConcurrentBag_RoundTrip()
        {
            var obj = new TestConcurrentBagObject
            {
                Numbers = new ConcurrentBag<int>(new[] { 10, 20, 30, 40 })
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestConcurrentBagObject>(reader);
            
            // Assert - Bag doesn't guarantee order, just count
            Assert.Equal(4, result!.Numbers!.Count);
            Assert.Contains(10, result.Numbers!);
            Assert.Contains(20, result.Numbers!);
            Assert.Contains(30, result.Numbers!);
            Assert.Contains(40, result.Numbers!);
        }
        
        #endregion
        
        #region Complex Nested Tests
        
        [MessagePackObject]
        public class TestComplexCollections
        {
            [Key(0)]
            public Dictionary<string, List<int>>? GroupedData { get; set; }
            
            [Key(1)]
            public List<Dictionary<int, string>>? ListOfDicts { get; set; }
            
            [Key(2)]
            public HashSet<Queue<string>>? SetOfQueues { get; set; }
        }
        
        [Fact]
        public void Serialize_ComplexNestedCollections_RoundTrip()
        {
            // Arrange - Ultra complex nesting
            var obj = new TestComplexCollections
            {
                GroupedData = new Dictionary<string, List<int>>
                {
                    { "evens", new List<int> { 2, 4, 6 } },
                    { "odds", new List<int> { 1, 3, 5 } }
                },
                ListOfDicts = new List<Dictionary<int, string>>
                {
                    new Dictionary<int, string> { { 1, "a" }, { 2, "b" } },
                    new Dictionary<int, string> { { 3, "c" } }
                },
                SetOfQueues = new HashSet<Queue<string>>
                {
                    new Queue<string>(new[] { "q1item1", "q1item2" })
                }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestComplexCollections>(reader);
            
            // Assert
            Assert.Equal(2, result!.GroupedData!.Count);
            Assert.Equal(3, result.GroupedData!["evens"].Count);
            Assert.Equal(2, result.GroupedData!["evens"][0]);
            
            Assert.Equal(2, result.ListOfDicts!.Count);
            Assert.Equal("a", result.ListOfDicts![0][1]);
            Assert.Equal("c", result.ListOfDicts![1][3]);
            
            Assert.Single(result.SetOfQueues!);
        }
        
        #endregion
    }
}
