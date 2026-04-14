using Xunit;
using Fdp.Kernel.FlightRecorder;
using MessagePack;
using System.IO;
using System;
using System.Collections.Generic;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests for FdpPolymorphicSerializer - handles serialization of interface/base class types
    /// with runtime type discrimination via [FdpPolymorphicType(id)] attribute.
    /// </summary>
    public class FdpPolymorphicSerializerTests
    {
        #region Test Types - Command Pattern (Common in AI systems)
        
        public interface ICommand
        {
            int CommandId { get; set; }
        }
        
        [MessagePackObject]
        [FdpPolymorphicType(1)]
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
        [FdpPolymorphicType(2)]
        public class AttackCommand : ICommand
        {
            [Key(0)]
            public int CommandId { get; set; }
            
            [Key(1)]
            public int TargetId { get; set; }
        }
        
        [MessagePackObject]
        [FdpPolymorphicType(3)]
        public class FormationCommand : ICommand
        {
            [Key(0)]
            public int CommandId { get; set; }
            
            [Key(1)]
            public string? FormationType { get; set; }
            
            [Key(2)]
            public int[]? UnitIds { get; set; }
        }
        
        #endregion
        
        #region Basic Polymorphic Writing Tests
        
        [Fact]
        public void Write_MoveCommand_WritesTypeIdAndData()
        {
            // Arrange
            var cmd = new MoveCommand { CommandId = 1, X = 10.5f, Y = 20.5f };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpPolymorphicSerializer.Write(writer, cmd);
            
            // Assert
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            byte typeId = reader.ReadByte();
            Assert.Equal(1, typeId); // MoveCommand's ID
        }
        
        [Fact]
        public void Write_AttackCommand_WritesCorrectTypeId()
        {
            // Arrange
            var cmd = new AttackCommand { CommandId = 2, TargetId = 999 };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpPolymorphicSerializer.Write(writer, cmd);
            
            // Assert
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            byte typeId = reader.ReadByte();
            Assert.Equal(2, typeId); // AttackCommand's ID
        }
        
        [Fact]
        public void Write_NullObject_WritesZeroTypeId()
        {
            // Arrange
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpPolymorphicSerializer.Write(writer, null!);
            
            // Assert
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            byte typeId = reader.ReadByte();
            Assert.Equal(0, typeId); // Null marker
        }
        
        #endregion
        
        #region Basic Polymorphic Reading Tests
        
        [Fact]
        public void Read_MoveCommand_RestoresCorrectType()
        {
            // Arrange
            var original = new MoveCommand { CommandId = 1, X = 10.5f, Y = 20.5f };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            FdpPolymorphicSerializer.Write(writer, original);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            
            // Act
            var result = FdpPolymorphicSerializer.Read(reader);
            
            // Assert
            Assert.NotNull(result);
            Assert.IsType<MoveCommand>(result);
            var moveCmd = (MoveCommand)result;
            Assert.Equal(1, moveCmd.CommandId);
            Assert.Equal(10.5f, moveCmd.X, precision: 5);
            Assert.Equal(20.5f, moveCmd.Y, precision: 5);
        }
        
        [Fact]
        public void Read_AttackCommand_RestoresCorrectType()
        {
            // Arrange
            var original = new AttackCommand { CommandId = 2, TargetId = 999 };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            FdpPolymorphicSerializer.Write(writer, original);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            
            // Act
            var result = FdpPolymorphicSerializer.Read(reader);
            
            // Assert
            Assert.NotNull(result);
            Assert.IsType<AttackCommand>(result);
            var attackCmd = (AttackCommand)result;
            Assert.Equal(2, attackCmd.CommandId);
            Assert.Equal(999, attackCmd.TargetId);
        }
        
        [Fact]
        public void Read_NullMarker_ReturnsNull()
        {
            // Arrange
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            FdpPolymorphicSerializer.Write(writer, null!);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            
            // Act
            var result = FdpPolymorphicSerializer.Read(reader);
            
            // Assert
            Assert.Null(result);
        }
        
        #endregion
        
        #region Mixed Type Sequence Tests
        
        [Fact]
        public void WriteAndRead_MixedCommandSequence_RestoresCorrectly()
        {
            // Arrange
            var commands = new ICommand[]
            {
                new MoveCommand { CommandId = 1, X = 5, Y = 10 },
                new AttackCommand { CommandId = 2, TargetId = 100 },
                null!,
                new FormationCommand { CommandId = 3, FormationType = "Line", UnitIds = new[] { 1, 2, 3 } },
                new MoveCommand { CommandId = 4, X = 15, Y = 20 }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act - Write sequence
            foreach (var cmd in commands)
            {
                FdpPolymorphicSerializer.Write(writer, cmd);
            }
            
            // Read back
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var results = new List<ICommand>();
            for (int i = 0; i < commands.Length; i++)
            {
                results.Add((ICommand)FdpPolymorphicSerializer.Read(reader)!);
            }
            
            // Assert
            Assert.Equal(5, results.Count);
            Assert.IsType<MoveCommand>(results[0]);
            Assert.IsType<AttackCommand>(results[1]);
            Assert.Null(results[2]);
            Assert.IsType<FormationCommand>(results[3]);
            Assert.IsType<MoveCommand>(results[4]);
            
            var formation = (FormationCommand)results[3]!;
            Assert.Equal("Line", formation!.FormationType);
            Assert.Equal(3, formation.UnitIds!.Length);
        }
        
        #endregion
        
        #region Complex Nested Data Tests
        
        [Fact]
        public void WriteAndRead_ComplexNestedData_PreservesStructure()
        {
            // Arrange - FormationCommand with nested arrays
            var cmd = new FormationCommand
            {
                CommandId = 100,
                FormationType = "Wedge",
                UnitIds = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act
            FdpPolymorphicSerializer.Write(writer, cmd);
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = (FormationCommand)FdpPolymorphicSerializer.Read(reader)!;
            
            // Assert
            Assert.Equal(100, result!.CommandId);
            Assert.Equal("Wedge", result.FormationType);
            Assert.Equal(10, result.UnitIds!.Length);
            Assert.Equal(1, result.UnitIds![0]);
            Assert.Equal(10, result.UnitIds![9]);
        }
        
        #endregion
        
        #region Error Handling Tests
        
        [Fact]
        public void Write_UnregisteredType_ThrowsException()
        {
            // Arrange - type without [FdpPolymorphicType] attribute
            var unregistered = new {Id = 1, Name = "Test" };
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
            {
                FdpPolymorphicSerializer.Write(writer, unregistered);
            });
        }
        
        [Fact]
        public void Read_UnknownTypeId_ThrowsException()
        {
            // Arrange - manually write invalid type ID
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write((byte)255); // Invalid type ID
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            
            // Act & Assert
            Assert.Throws<KeyNotFoundException>(() =>
            {
                FdpPolymorphicSerializer.Read(reader);
            });
        }
        
        #endregion
        
        #region Type Registry Tests
        
        [Fact]
        public void Registry_FindsAllPolymorphicTypes()
        {
            // This test verifies the static constructor scans assemblies correctly
            
            // Act - Access the registry (triggers static constructor if not already done)
            var hasMove = FdpPolymorphicSerializer.IsTypeRegistered<MoveCommand>();
            var hasAttack = FdpPolymorphicSerializer.IsTypeRegistered<AttackCommand>();
            var hasFormation = FdpPolymorphicSerializer.IsTypeRegistered<FormationCommand>();
            
            // Assert
            Assert.True(hasMove, "MoveCommand should be registered");
            Assert.True(hasAttack, "AttackCommand should be registered");
            Assert.True(hasFormation, "FormationCommand should be registered");
        }
        
        [Fact]
        public void Registry_RejectsConcreteTypeWithoutAttribute()
        {
            // Act - MoveCommand has the attribute, but a random string doesn't
            var hasString = FdpPolymorphicSerializer.IsTypeRegistered<string>();
            
            // Assert
            Assert.False(hasString, "string should NOT be polymorphic");
        }
        
        #endregion
        
        #region Performance Tests
        
        [Fact]
        public void WriteAndRead_LargeSequence_CompletesEfficiently()
        {
            // Arrange - 1000 mixed commands
            var commands = new List<ICommand>();
            for (int i = 0; i < 1000; i++)
            {
                if (i % 3 == 0)
                    commands.Add(new MoveCommand { CommandId = i, X = i * 1.1f, Y = i * 2.2f });
                else if (i % 3 == 1)
                    commands.Add(new AttackCommand { CommandId = i, TargetId = i * 10 });
                else
                    commands.Add(new FormationCommand { CommandId = i, FormationType = $"Form{i}", UnitIds = new[] { i } });
            }
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act - Write
            foreach (var cmd in commands)
            {
                FdpPolymorphicSerializer.Write(writer, cmd);
            }
            
            // Read back
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var results = new List<ICommand>();
            for (int i = 0; i < 1000; i++)
            {
                results.Add((ICommand)FdpPolymorphicSerializer.Read(reader)!);
            }
            
            // Assert - spot check
            Assert.Equal(1000, results.Count);
            Assert.IsType<MoveCommand>(results[0]);
            Assert.IsType<AttackCommand>(results[1]);
            Assert.IsType<FormationCommand>(results[2]);
            
            var lastMove = results[999] as MoveCommand;
            Assert.NotNull(lastMove);
            Assert.Equal(999, lastMove.CommandId);
        }
        
        #endregion
        
        #region EXTREME Stress Test - Kitchen Sink
        
        // Ultra-complex type combining ALL difficult features
        [MessagePackObject]
        [FdpPolymorphicType(10)]
        public class UltraComplexCommand : ICommand
        {
            [Key(0)]
            public int CommandId { get; set; }
            
            [Key(1)]
            public string? Name { get; set; } // Can be null
            
            [Key(2)]
            public List<string>? Tags { get; set; } // List with nulls
            
            [Key(3)]
            public ICommand[]? NestedCommands { get; set; } // Polymorphic array with nulls
            
            [Key(4)]
            public List<List<int>>? Matrix { get; set; } // Nested lists
            
            [Key(5)]
            public MoveCommand[][]? JaggedMoves { get; set; } // Arrays of arrays with nulls
            
            [Key(6)]
            public List<ICommand>[]? ArrayOfPolymorphicLists { get; set; } // Array of lists of polymorphic
        }
        
        [Fact]
        public void EXTREME_PolymorphicNestingWithNulls_FullRoundTrip()
        {
            // Arrange - Create the most complex possible structure
            var ultraComplex = new UltraComplexCommand
            {
                CommandId = 42,
                Name = null!, // Null string
                Tags = new List<string> { "alpha", null!, "beta", null!, "gamma" }, // List with nulls
                NestedCommands = new ICommand[]
                {
                    new MoveCommand { CommandId = 1, X = 1.1f, Y = 2.2f },
                    null!, // Null polymorphic element
                    new AttackCommand { CommandId = 2, TargetId = 100 },
                    new FormationCommand 
                    { 
                        CommandId = 3, 
                        FormationType = null!, // Null string in nested object
                        UnitIds = new[] { 1, 2, 3 }
                    },
                    null!
                },
                Matrix = new List<List<int>>
                {
                    new List<int> { 1, 2, 3 },
                    new List<int>(), // Empty list
                    new List<int> { 4, 5 },
                    null! // Null list in list
                },
                JaggedMoves = new MoveCommand[][]
                {
                    new MoveCommand[] { new MoveCommand { CommandId = 10, X = 1, Y = 2 }, null! },
                    null!, // Null array within array
                    new MoveCommand[] { null!, null!, new MoveCommand { CommandId = 11, X = 3, Y = 4 } }
                },
                ArrayOfPolymorphicLists = new List<ICommand>[]
                {
                    new List<ICommand> 
                    { 
                        new MoveCommand { CommandId = 20, X = 5, Y = 6 },
                        null!,
                        new AttackCommand { CommandId = 21, TargetId = 200 }
                    },
                    null!, // Null list in array
                    new List<ICommand> { null! } // List containing only null
                }
            };
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Act - Write the beast
            FdpPolymorphicSerializer.Write(writer, ultraComplex);
            
            // Read it back
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            var result = (UltraComplexCommand)FdpPolymorphicSerializer.Read(reader)!;
            
            // Assert - Verify EVERYTHING survived
            Assert.NotNull(result);
            Assert.Equal(42, result.CommandId);
            Assert.Null(result.Name);
            
            // Tags
            Assert.Equal(5, result!.Tags!.Count);
            Assert.Equal("alpha", result.Tags![0]);
            Assert.Null(result.Tags![1]);
            Assert.Equal("beta", result.Tags![2]);
            Assert.Null(result.Tags![3]);
            Assert.Equal("gamma", result.Tags![4]);
            
            // Nested polymorphic commands
            Assert.Equal(5, result.NestedCommands!.Length);
            Assert.IsType<MoveCommand>(result.NestedCommands![0]);
            Assert.Null(result.NestedCommands![1]);
            Assert.IsType<AttackCommand>(result.NestedCommands![2]);
            var formation = result.NestedCommands![3] as FormationCommand;
            Assert.NotNull(formation);
            Assert.Null(formation!.FormationType); // Null string in nested object
            Assert.Equal(3, formation.UnitIds!.Length);
            Assert.Null(result.NestedCommands![4]);
            
            // Matrix (list of lists)
            Assert.Equal(4, result.Matrix!.Count);
            Assert.Equal(3, result.Matrix![0]!.Count);
            Assert.Empty(result.Matrix![1]!);
            Assert.Equal(2, result.Matrix![2]!.Count);
            Assert.Null(result.Matrix![3]);
            
            // Jagged arrays
            Assert.Equal(3, result.JaggedMoves!.Length);
            Assert.Equal(2, result.JaggedMoves![0]!.Length);
            Assert.NotNull(result.JaggedMoves![0]![0]);
            Assert.Null(result.JaggedMoves![0]![1]);
            Assert.Null(result.JaggedMoves![1]); // Null array
            Assert.Equal(3, result.JaggedMoves![2]!.Length);
            Assert.Null(result.JaggedMoves![2]![0]);
            Assert.Null(result.JaggedMoves![2]![1]);
            Assert.NotNull(result.JaggedMoves![2]![2]);
            
            // Array of polymorphic lists
            Assert.Equal(3, result.ArrayOfPolymorphicLists!.Length);
            Assert.Equal(3, result.ArrayOfPolymorphicLists![0]!.Count);
            Assert.IsType<MoveCommand>(result.ArrayOfPolymorphicLists![0]![0]);
            Assert.Null(result.ArrayOfPolymorphicLists![0]![1]);
            Assert.IsType<AttackCommand>(result.ArrayOfPolymorphicLists![0]![2]);
            Assert.Null(result.ArrayOfPolymorphicLists![1]); // Null list
            Assert.Single(result.ArrayOfPolymorphicLists![2]!);
            Assert.Null(result.ArrayOfPolymorphicLists![2]![0]); // List containing null
        }
        
        #endregion
    }
}
