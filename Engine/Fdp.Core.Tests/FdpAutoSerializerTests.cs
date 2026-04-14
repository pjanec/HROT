using Xunit;
using Fdp.Kernel.FlightRecorder;
using MessagePack;
using System.IO;
using System;
using System.Collections.Generic;

namespace Fdp.Tests
{
    [MessagePackObject]
    public class TestSimpleObject
    {
        [Key(0)]
        public int Id { get; set; }
        
        [Key(1)]
        public string? Name { get; set; }
    }

    [MessagePackObject]
    public class TestListObject
    {
        [Key(0)]
        public List<int>? Numbers { get; set; }
    }

    public class FdpAutoSerializerTests
    {
        [Fact]
        public void Serialize_SimpleObject_WritesCorrectValues()
        {
            // Arrange
            var obj = new TestSimpleObject { Id = 42, Name = "Test" };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Act
            FdpAutoSerializer.Serialize(obj, writer);

            // Assert
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            
            // Expected format: 
            // Id (int)
            // Name (bool hasValue, then string)
            
            int id = reader.ReadInt32();
            Assert.Equal(42, id);
            
            bool hasName = reader.ReadBoolean();
            Assert.True(hasName);
            string name = reader.ReadString();
            Assert.Equal("Test", name);
        }

        [Fact]
        public void Deserialize_SimpleObject_RestoresValues()
        {
            // Arrange
            var obj = new TestSimpleObject { Id = 99, Name = "Restore" };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);

            // Act
            var result = FdpAutoSerializer.Deserialize<TestSimpleObject>(reader);

            // Assert
            Assert.Equal(99, result.Id);
            Assert.Equal("Restore", result.Name);
        }
        
        [Fact]
        public void Serialize_ListObject_RoundTrip()
        {
            // Arrange
            var obj = new TestListObject { Numbers = new List<int> { 1, 2, 3 } };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestListObject>(reader);
            
            // Assert
            Assert.NotNull(result.Numbers);
            Assert.Equal(3, result.Numbers.Count);
            Assert.Equal(1, result.Numbers[0]);
            Assert.Equal(2, result.Numbers[1]);
            Assert.Equal(3, result.Numbers[2]);
        }
        
        #region Null Handling Tests (Critical for Sparse Data)
        
        [Fact]
        public void Serialize_NullString_HandlesCorrectly()
        {
            // Arrange
            var obj = new TestSimpleObject { Id = 10, Name = null };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            // Assert
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            int id = reader.ReadInt32();
            bool hasName = reader.ReadBoolean();
            
            Assert.Equal(10, id);
            Assert.False(hasName); // Name should be marked as null
        }
        
        [Fact]
        public void Deserialize_NullString_RestoresNull()
        {
            // Arrange
            var obj = new TestSimpleObject { Id = 20, Name = null };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            
            // Act
            var result = FdpAutoSerializer.Deserialize<TestSimpleObject>(reader);
            
            // Assert
            Assert.Equal(20, result.Id);
            Assert.Null(result.Name);
        }
        
        [Fact]
        public void Serialize_NullList_HandlesCorrectly()
        {
            // Arrange
            var obj = new TestListObject { Numbers = null };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            // Assert
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            bool hasList = reader.ReadBoolean();
            
            Assert.False(hasList);
        }
        
        [Fact]
        public void Deserialize_NullList_RestoresNull()
        {
            // Arrange
            var obj = new TestListObject { Numbers = null };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            
            // Act
            var result = FdpAutoSerializer.Deserialize<TestListObject>(reader);
            
            // Assert
            Assert.Null(result.Numbers);
        }
        
        #endregion
        
        #region Empty Collection Tests
        
        [Fact]
        public void Serialize_EmptyList_RoundTrip()
        {
            // Arrange
            var obj = new TestListObject { Numbers = new List<int>() };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestListObject>(reader);
            
            // Assert
            Assert.NotNull(result.Numbers);
            Assert.Empty(result.Numbers);
        }
        
        #endregion
        
        #region Nested Object Tests
        
        [MessagePackObject]
        public class TestNestedObject
        {
            [Key(0)]
            public TestSimpleObject? Inner { get; set; }
            
            [Key(1)]
            public int OuterId { get; set; }
        }
        
        [Fact]
        public void Serialize_NestedObject_RoundTrip()
        {
            // Arrange
            var obj = new TestNestedObject
            {
                OuterId = 100,
                Inner = new TestSimpleObject { Id = 42, Name = "Nested" }
            };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestNestedObject>(reader);
            
            // Assert
            Assert.Equal(100, result.OuterId);
            Assert.NotNull(result.Inner);
            Assert.Equal(42, result.Inner.Id);
            Assert.Equal("Nested", result.Inner.Name);
        }
        
        [Fact]
        public void Serialize_NestedObjectNull_HandlesCorrectly()
        {
            // Arrange
            var obj = new TestNestedObject
            {
                OuterId = 200,
                Inner = null
            };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestNestedObject>(reader);
            
            // Assert
            Assert.Equal(200, result.OuterId);
            Assert.Null(result.Inner);
        }
        
        #endregion
        
        #region Array Support Tests
        
        [MessagePackObject]
        public class TestArrayObject
        {
            [Key(0)]
            public int[]? Values { get; set; }
        }
        
        [Fact]
        public void Serialize_IntArray_RoundTrip()
        {
            // Arrange
            var obj = new TestArrayObject { Values = new int[] { 10, 20, 30, 40 } };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestArrayObject>(reader);
            
            // Assert
            Assert.NotNull(result.Values);
            Assert.Equal(4, result.Values.Length);
            Assert.Equal(10, result.Values[0]);
            Assert.Equal(20, result.Values[1]);
            Assert.Equal(30, result.Values[2]);
            Assert.Equal(40, result.Values[3]);
        }
        
        [Fact]
        public void Serialize_EmptyArray_RoundTrip()
        {
            // Arrange
            var obj = new TestArrayObject { Values = new int[0] };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestArrayObject>(reader);
            
            // Assert
            Assert.NotNull(result.Values);
            Assert.Empty(result.Values);
        }
        
        [Fact]
        public void Serialize_NullArray_RoundTrip()
        {
            // Arrange
            var obj = new TestArrayObject { Values = null };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestArrayObject>(reader);
            
            // Assert
            Assert.Null(result.Values);
        }
        
        #endregion
        
        #region Large List Tests (Performance)
        
        [Fact]
        public void Serialize_LargeList_HandlesCorrectly()
        {
            // Arrange
            var numbers = new List<int>();
            for (int i = 0; i < 10000; i++)
            {
                numbers.Add(i);
            }
            var obj = new TestListObject { Numbers = numbers };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestListObject>(reader);
            
            // Assert
            Assert.Equal(10000, result!.Numbers!.Count);
            Assert.Equal(0, result.Numbers![0]);
            Assert.Equal(9999, result.Numbers![9999]);
        }
        
        #endregion
        
        #region Complex Scenario Tests
        
        [MessagePackObject]
        public class TestComplexObject
        {
            [Key(0)]
            public int Id { get; set; }
            
            [Key(1)]
            public string? Name { get; set; }
            
            [Key(2)]
            public List<string>? Tags { get; set; }
            
            [Key(3)]
            public TestSimpleObject? Metadata { get; set; }
            
            [Key(4)]
            public float[]? Coordinates { get; set; }
        }
        
        [Fact]
        public void Serialize_ComplexObject_FullRoundTrip()
        {
            // Arrange - Simulating AI squad order with various data types
            var obj = new TestComplexObject
            {
                Id = 777,
                Name = "Squad Alpha",
                Tags = new List<string> { "elite", "cavalry", "mobile" },
                Metadata = new TestSimpleObject { Id = 1, Name = "Commander" },
                Coordinates = new float[] { 100.5f, 200.75f, 50.25f }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestComplexObject>(reader);
            
            // Assert
            Assert.Equal(777, result.Id);
            Assert.Equal("Squad Alpha", result.Name);
            
            Assert.NotNull(result.Tags);
            Assert.Equal(3, result.Tags.Count);
            Assert.Equal("elite", result.Tags[0]);
            Assert.Equal("cavalry", result.Tags[1]);
            Assert.Equal("mobile", result.Tags[2]);
            
            Assert.NotNull(result.Metadata);
            Assert.Equal(1, result.Metadata.Id);
            Assert.Equal("Commander", result.Metadata.Name);
            
            Assert.NotNull(result.Coordinates);
            Assert.Equal(3, result.Coordinates.Length);
            Assert.Equal(100.5f, result.Coordinates[0]);
            Assert.Equal(200.75f, result.Coordinates[1]);
            Assert.Equal(50.25f, result.Coordinates[2]);
        }
        
        [Fact]
        public void Serialize_ComplexObjectWithNulls_HandlesCorrectly()
        {
            // Arrange - Sparse data scenario
            var obj = new TestComplexObject
            {
                Id = 888,
                Name = null,
                Tags = null,
                Metadata = null,
                Coordinates = null
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestComplexObject>(reader);
            
            // Assert
            Assert.Equal(888, result.Id);
            Assert.Null(result.Name);
            Assert.Null(result.Tags);
            Assert.Null(result.Metadata);
            Assert.Null(result.Coordinates);
        }
        
        #endregion
        
        #region Multiple Serialize/Deserialize Tests (Caching)
        
        [Fact]
        public void Serialize_MultipleCalls_ReusesCachedSerializer()
        {
            // Arrange
            var obj1 = new TestSimpleObject { Id = 1, Name = "First" };
            var obj2 = new TestSimpleObject { Id = 2, Name = "Second" };
            
            using var ms1 = new MemoryStream();
            using var writer1 = new BinaryWriter(ms1);
            using var ms2 = new MemoryStream();
            using var writer2 = new BinaryWriter(ms2);
            
            // Act - Second call should use cached serializer
            FdpAutoSerializer.Serialize(obj1, writer1);
            FdpAutoSerializer.Serialize(obj2, writer2);
            
            // Assert - Both should serialize correctly
            ms1.Position = 0;
            using var reader1 = new BinaryReader(ms1);
            var result1 = FdpAutoSerializer.Deserialize<TestSimpleObject>(reader1);
            Assert.Equal(1, result1.Id);
            Assert.Equal("First", result1.Name);
            
            ms2.Position = 0;
            using var reader2 = new BinaryReader(ms2);
            var result2 = FdpAutoSerializer.Deserialize<TestSimpleObject>(reader2);
            Assert.Equal(2, result2.Id);
            Assert.Equal("Second", result2.Name);
        }
        
        #endregion
        
        #region Nested Collections Tests (Critical for Complex Data)
        
        [MessagePackObject]
        public class TestListOfListsObject
        {
            [Key(0)]
            public List<List<int>>? Matrix { get; set; }
        }
        
        [Fact]
        public void Serialize_ListOfLists_RoundTrip()
        {
            // Arrange - 2D matrix structure
            var obj = new TestListOfListsObject
            {
                Matrix = new List<List<int>>
                {
                    new List<int> { 1, 2, 3 },
                    new List<int> { 4, 5, 6 },
                    new List<int> { 7, 8, 9 }
                }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestListOfListsObject>(reader);
            
            // Assert
            Assert.NotNull(result.Matrix);
            Assert.Equal(3, result.Matrix.Count);
            
            Assert.Equal(3, result.Matrix[0].Count);
            Assert.Equal(1, result.Matrix[0][0]);
            Assert.Equal(2, result.Matrix[0][1]);
            Assert.Equal(3, result.Matrix[0][2]);
            
            Assert.Equal(3, result.Matrix[1].Count);
            Assert.Equal(4, result.Matrix[1][0]);
            Assert.Equal(9, result.Matrix[2][2]);
        }
        
        [MessagePackObject]
        public class TestArrayOfArraysObject
        {
            [Key(0)]
            public int[][]? JaggedArray { get; set; }
        }
        
        [Fact]
        public void Serialize_ArrayOfArrays_RoundTrip()
        {
            // Arrange - Jagged array (common in pathfinding/formations)
            var obj = new TestArrayOfArraysObject
            {
                JaggedArray = new int[][]
                {
                    new int[] { 1, 2 },
                    new int[] { 3, 4, 5, 6 },
                    new int[] { 7 }
                }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestArrayOfArraysObject>(reader);
            
            // Assert
            Assert.NotNull(result.JaggedArray);
            Assert.Equal(3, result.JaggedArray.Length);
            
            Assert.Equal(2, result.JaggedArray[0].Length);
            Assert.Equal(1, result.JaggedArray[0][0]);
            Assert.Equal(2, result.JaggedArray[0][1]);
            
            Assert.Equal(4, result.JaggedArray[1].Length);
            Assert.Equal(3, result.JaggedArray[1][0]);
            
            Assert.Single(result.JaggedArray[2]);
            Assert.Equal(7, result.JaggedArray[2][0]);
        }
        
        [MessagePackObject]
        public class TestMixedNestedCollections
        {
            [Key(0)]
            public List<int[]>? ListOfArrays { get; set; }
            
            [Key(1)]
            public List<TestSimpleObject>[]? ArrayOfLists { get; set; }
        }
        
        [Fact]
        public void Serialize_MixedNestedCollections_RoundTrip()
        {
            // Arrange - Ultra complex: List of arrays AND array of lists
            var obj = new TestMixedNestedCollections
            {
                ListOfArrays = new List<int[]>
                {
                    new int[] { 1, 2, 3 },
                    new int[] { 4, 5 }
                },
                ArrayOfLists = new List<TestSimpleObject>[]
                {
                    new List<TestSimpleObject>
                    {
                        new TestSimpleObject { Id = 1, Name = "A" },
                        new TestSimpleObject { Id = 2, Name = "B" }
                    }
                }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestMixedNestedCollections>(reader);
            
            // Assert
            Assert.NotNull(result.ListOfArrays);
            Assert.Equal(2, result.ListOfArrays.Count);
            Assert.Equal(3, result.ListOfArrays[0].Length);
            Assert.Equal(1, result.ListOfArrays[0][0]);
            
            Assert.NotNull(result.ArrayOfLists);
            Assert.Single(result.ArrayOfLists);
            Assert.Equal(2, result.ArrayOfLists[0].Count);
            Assert.Equal("A", result.ArrayOfLists[0][0].Name);
            Assert.Equal("B", result.ArrayOfLists[0][1].Name);
        }
        
        #endregion
        
        #region Value Type Collections Tests
        
        [MessagePackObject]
        public class TestFloatArrayObject
        {
            [Key(0)]
            public float[]? Coordinates { get; set; }
        }
        
        [Fact]
        public void Serialize_FloatArray_PreservesPrecision()
        {
            // Arrange
            var obj = new TestFloatArrayObject
            {
                Coordinates = new float[] { 1.1f, 2.2f, 3.3f, 4.4f }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestFloatArrayObject>(reader);
            
            // Assert
            Assert.Equal(4, result!.Coordinates!.Length);
            Assert.Equal(1.1f, result.Coordinates![0], precision: 5);
            Assert.Equal(2.2f, result.Coordinates![1], precision: 5);
            Assert.Equal(3.3f, result.Coordinates![2], precision: 5);
            Assert.Equal(4.4f, result.Coordinates![3], precision: 5);
        }
        
        #endregion
        
        #region Polymorphism Tests (Interface/Base Class)
        
        // NOTE: These tests verify the NEED for polymorphic serialization.
        // FdpPolymorphicSerializer is designed but may not be fully integrated yet.
        
        public interface ICommand
        {
            int CommandId { get; set; }
        }
        
        [MessagePackObject]
        public class MoveCommand : ICommand
        {
            [Key(0)]
            public int CommandId { get; set; }
            
            [Key(1)]
            public float X { get; set; }
            
            [Key(2)]
            public float Y { get; set; }
        }
        
        [MessagePackObject]
        public class AttackCommand : ICommand
        {
            [Key(0)]
            public int CommandId { get; set; }
            
            [Key(1)]
            public int TargetId { get; set; }
        }
        
        [MessagePackObject]
        public class TestPolymorphicHolder
        {
            [Key(0)]
            public MoveCommand? SpecificCommand { get; set; }
            
            // Note: Polymorph ic lists would use FdpPolymorphicSerializer
            // [Key(1)]
            // public List<ICommand> Commands { get; set; }
        }
        
        [Fact]
        public void Serialize_ConcretePolymorphicType_Works()
        {
            // Arrange - Concrete type (not through interface)
            var obj = new TestPolymorphicHolder
            {
                SpecificCommand = new MoveCommand { CommandId = 1, X = 10.5f, Y = 20.5f }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestPolymorphicHolder>(reader);
            
            // Assert
            Assert.NotNull(result.SpecificCommand);
            Assert.Equal(1, result.SpecificCommand.CommandId);
            Assert.Equal(10.5f, result.SpecificCommand.X, precision: 5);
            Assert.Equal(20.5f, result.SpecificCommand.Y, precision: 5);
        }
        
        // TODO: Add when FdpPolymorphicSerializer is fully integrated
        // [Fact(Skip = "FdpPolymorphicSerializer integration pending")]
        // public void Serialize_PolymorphicList_RoundTrip()
        // {
        //     // This would test List<ICommand> containing MoveCommand and AttackCommand
        // }
        
        #endregion
        
        #region String Collection Tests
        
        [MessagePackObject]
        public class TestStringArrayObject
        {
            [Key(0)]
            public string[]? Names { get; set; }
        }
        
        [Fact]
        public void Serialize_StringArray_RoundTrip()
        {
            // Arrange
            var obj = new TestStringArrayObject
            {
                Names = new string[] { "Alpha", "Bravo", "Charlie", null!, "Delta" }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestStringArrayObject>(reader);
            
            // Assert
            Assert.Equal(5, result!.Names!.Length);
            Assert.Equal("Alpha", result.Names![0]);
            Assert.Equal("Bravo", result.Names![1]);
            Assert.Equal("Charlie", result.Names![2]);
            Assert.Null(result.Names![3]); // Null in middle
            Assert.Equal("Delta", result.Names![4]);
        }
        
        [MessagePackObject]
        public class TestListOfStringsObject
        {
            [Key(0)]
            public List<string>? Tags { get; set; }
        }
        
        [Fact]
        public void Serialize_ListOfStrings_WithNulls_RoundTrip()
        {
            // Arrange
            var obj = new TestListOfStringsObject
            {
                Tags = new List<string> { "tag1", null!, "tag2", "tag3", null! }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestListOfStringsObject>(reader);
            
            // Assert
            Assert.Equal(5, result!.Tags!.Count);
            Assert.Equal("tag1", result.Tags![0]);
            Assert.Null(result.Tags![1]);
            Assert.Equal("tag2", result.Tags![2]);
            Assert.Equal("tag3", result.Tags![3]);
            Assert.Null(result.Tags![4]);
        }
        
        #endregion
        
        #region Deep Nesting Tests (Stress Test)
        
        [MessagePackObject]
        public class TestDeeplyNestedObject
        {
            [Key(0)]
            public int Level { get; set; }
            
            [Key(1)]
            public TestDeeplyNestedObject? Child { get; set; }
        }
        
        [Fact]
        public void Serialize_DeeplyNestedObject_5Levels_RoundTrip()
        {
            // Arrange - 5 levels deep (simulating hierarchical structures)
            var obj = new TestDeeplyNestedObject
            {
                Level = 1,
                Child = new TestDeeplyNestedObject
                {
                    Level = 2,
                    Child = new TestDeeplyNestedObject
                    {
                        Level = 3,
                        Child = new TestDeeplyNestedObject
                        {
                            Level = 4,
                            Child = new TestDeeplyNestedObject
                            {
                                Level = 5,
                                Child = null
                            }
                        }
                    }
                }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpAutoSerializer.Serialize(obj, writer);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = FdpAutoSerializer.Deserialize<TestDeeplyNestedObject>(reader);
            
            // Assert
            Assert.Equal(1, result.Level);
            Assert.NotNull(result.Child);
            Assert.Equal(2, result.Child.Level);
            Assert.NotNull(result.Child.Child);
            Assert.Equal(3, result.Child.Child.Level);
            Assert.NotNull(result.Child.Child.Child);
            Assert.Equal(4, result.Child.Child.Child.Level);
            Assert.NotNull(result.Child.Child.Child.Child);
            Assert.Equal(5, result.Child.Child.Child.Child.Level);
            Assert.Null(result.Child.Child.Child.Child.Child);
        }
        
        #endregion
    }
}
