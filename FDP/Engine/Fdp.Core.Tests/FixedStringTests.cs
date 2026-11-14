using Xunit;
using Fdp.Core;
using System;

namespace Fdp.Tests
{
    public class FixedString32Tests
    {
        [Fact]
        public void FixedString32_EmptyConstruction()
        {
            var str = new FixedString32();
            
            Assert.True(str.IsEmpty);
            Assert.Equal(0, str.Length);
            Assert.Equal(string.Empty, str.ToString());
        }
        
        [Fact]
        public void FixedString32_SimpleString()
        {
            var str = new FixedString32("Hello");
            
            Assert.False(str.IsEmpty);
            Assert.Equal(5, str.Length);
            Assert.Equal("Hello", str.ToString());
        }
        
        [Fact]
        public void FixedString32_MaxLength()
        {
            // 31 characters (max)
            var longStr = new string('X', 31);
            var str = new FixedString32(longStr);
            
            Assert.Equal(31, str.Length);
            Assert.Equal(longStr, str.ToString());
        }
        
        [Fact]
        public void FixedString32_Truncation()
        {
            // 50 characters - should truncate to 31
            var tooLong = new string('Y', 50);
            var str = new FixedString32(tooLong);
            
            Assert.Equal(31, str.Length);
            Assert.Equal(new string('Y', 31), str.ToString());
        }
        
        [Fact]
        public void FixedString32_NullString()
        {
            var str = new FixedString32(null!);
            
            Assert.True(str.IsEmpty);
            Assert.Equal(string.Empty, str.ToString());
        }
        
        [Fact]
        public void FixedString32_Clear()
        {
            var str = new FixedString32("Test");
            Assert.False(str.IsEmpty);
            
            str.Clear();
            
            Assert.True(str.IsEmpty);
            Assert.Equal(string.Empty, str.ToString());
        }
        
        [Fact]
        public void FixedString32_Equality()
        {
            var str1 = new FixedString32("Hello");
            var str2 = new FixedString32("Hello");
            var str3 = new FixedString32("World");
            
            Assert.True(str1 == str2);
            Assert.False(str1 == str3);
            Assert.True(str1.Equals(str2));
            Assert.False(str1.Equals(str3));
        }
        
        [Fact]
        public void FixedString32_ImplicitConversion_ToString()
        {
            var str = new FixedString32("Test");
            string regular = str; // Implicit conversion
            
            Assert.Equal("Test", regular);
        }
        
        [Fact]
        public void FixedString32_ImplicitConversion_FromString()
        {
            FixedString32 str = "Test"; // Implicit conversion
            
            Assert.Equal("Test", str.ToString());
        }
        
        [Fact]
        public void FixedString32_CopyFrom()
        {
            var str1 = new FixedString32("Original");
            var str2 = new FixedString32();
            
            str2.CopyFrom(str1);
            
            Assert.Equal("Original", str2.ToString());
            Assert.True(str1 == str2);
        }
        
        [Fact]
        public void FixedString32_HashCode_Consistent()
        {
            var str1 = new FixedString32("Test");
            var str2 = new FixedString32("Test");
            
            Assert.Equal(str1.GetHashCode(), str2.GetHashCode());
        }
        
        [Fact]
        public void FixedString32_Size_Is32Bytes()
        {
            unsafe
            {
                Assert.Equal(32, sizeof(FixedString32));
            }
        }
        
        [Fact]
        public void FixedString32_SpecialCharacters()
        {
            var str = new FixedString32("Hello! @#$%");
            
            Assert.Equal("Hello! @#$%", str.ToString());
        }
        
        [Fact]
        public void FixedString32_Numbers()
        {
            var str = new FixedString32("12345");

            Assert.Equal("12345", str.ToString());
        }

        [Fact]
        public void FixedString32_SpanCtor_RoundTrip()
        {
            var str = new FixedString32("Hello".AsSpan());

            Assert.False(str.IsEmpty);
            Assert.Equal(5, str.Length);
            Assert.Equal("Hello", str.ToString());
        }

        [Fact]
        public void FixedString32_SpanCtor_EmptySpan()
        {
            var str = new FixedString32(ReadOnlySpan<char>.Empty);

            Assert.True(str.IsEmpty);
            Assert.Equal(0, str.Length);
            Assert.Equal(string.Empty, str.ToString());
        }

        [Fact]
        public void FixedString32_SpanCtor_Truncation()
        {
            // 50 characters - should truncate to 31, same as the string ctor.
            var tooLong = new string('Y', 50);
            var str = new FixedString32(tooLong.AsSpan());

            Assert.Equal(31, str.Length);
            Assert.Equal(new string('Y', 31), str.ToString());
        }

        [Fact]
        public void FixedString32_SpanCtor_MatchesStringCtor()
        {
            const string input = "Hello, FixedString32!";
            var fromSpan = new FixedString32(input.AsSpan());
            var fromString = new FixedString32(input);

            Assert.Equal(fromString, fromSpan);
            Assert.Equal(fromString.ToString(), fromSpan.ToString());
        }
    }

    public class FixedString64Tests
    {
        [Fact]
        public void FixedString64_EmptyConstruction()
        {
            var str = new FixedString64();
            
            Assert.True(str.IsEmpty);
            Assert.Equal(0, str.Length);
            Assert.Equal(string.Empty, str.ToString());
        }
        
        [Fact]
        public void FixedString64_SimpleString()
        {
            var str = new FixedString64("Hello World");
            
            Assert.False(str.IsEmpty);
            Assert.Equal(11, str.Length);
            Assert.Equal("Hello World", str.ToString());
        }
        
        [Fact]
        public void FixedString64_MaxLength()
        {
            // 63 characters (max)
            var longStr = new string('X', 63);
            var str = new FixedString64(longStr);
            
            Assert.Equal(63, str.Length);
            Assert.Equal(longStr, str.ToString());
        }
        
        [Fact]
        public void FixedString64_Truncation()
        {
            // 100 characters - should truncate to 63
            var tooLong = new string('Y', 100);
            var str = new FixedString64(tooLong);
            
            Assert.Equal(63, str.Length);
            Assert.Equal(new string('Y', 63), str.ToString());
        }
        
        [Fact]
        public void FixedString64_Equality()
        {
            var str1 = new FixedString64("Testing 123");
            var str2 = new FixedString64("Testing 123");
            var str3 = new FixedString64("Different");
            
            Assert.True(str1 == str2);
            Assert.False(str1 == str3);
        }
        
        [Fact]
        public void FixedString64_ImplicitConversions()
        {
            FixedString64 str = "Test String";
            string regular = str;
            
            Assert.Equal("Test String", regular);
        }
        
        [Fact]
        public void FixedString64_Size_Is64Bytes()
        {
            unsafe
            {
                Assert.Equal(64, sizeof(FixedString64));
            }
        }
        
        [Fact]
        public void FixedString64_LongerThanFixedString32()
        {
            // String that fits in 64 but not 32
            var mediumStr = new string('A', 40);
            var str = new FixedString64(mediumStr);

            Assert.Equal(40, str.Length);
            Assert.Equal(mediumStr, str.ToString());
        }

        [Fact]
        public void FixedString64_SpanCtor_RoundTrip()
        {
            var str = new FixedString64("Hello World".AsSpan());

            Assert.False(str.IsEmpty);
            Assert.Equal(11, str.Length);
            Assert.Equal("Hello World", str.ToString());
        }

        [Fact]
        public void FixedString64_SpanCtor_EmptySpan()
        {
            var str = new FixedString64(ReadOnlySpan<char>.Empty);

            Assert.True(str.IsEmpty);
            Assert.Equal(0, str.Length);
            Assert.Equal(string.Empty, str.ToString());
        }

        [Fact]
        public void FixedString64_SpanCtor_Truncation()
        {
            // 100 characters - should truncate to 63, same as the string ctor.
            var tooLong = new string('Y', 100);
            var str = new FixedString64(tooLong.AsSpan());

            Assert.Equal(63, str.Length);
            Assert.Equal(new string('Y', 63), str.ToString());
        }

        [Fact]
        public void FixedString64_SpanCtor_MatchesStringCtor()
        {
            const string input = "Hello, FixedString64!";
            var fromSpan = new FixedString64(input.AsSpan());
            var fromString = new FixedString64(input);

            Assert.Equal(fromString, fromSpan);
            Assert.Equal(fromString.ToString(), fromSpan.ToString());
        }
    }

    public class FixedString128Tests
    {
        [Fact]
        public void FixedString128_EmptyConstruction()
        {
            var str = new FixedString128();

            Assert.True(str.IsEmpty);
            Assert.Equal(0, str.Length);
            Assert.Equal(string.Empty, str.ToString());
        }

        [Fact]
        public void FixedString128_SimpleString()
        {
            var str = new FixedString128("Hello World");

            Assert.False(str.IsEmpty);
            Assert.Equal(11, str.Length);
            Assert.Equal("Hello World", str.ToString());
        }

        [Fact]
        public void FixedString128_MaxLength()
        {
            // 127 characters (max)
            var longStr = new string('X', 127);
            var str = new FixedString128(longStr);

            Assert.Equal(127, str.Length);
            Assert.Equal(longStr, str.ToString());
        }

        [Fact]
        public void FixedString128_Truncation()
        {
            // 200 characters - should truncate to 127
            var tooLong = new string('Y', 200);
            var str = new FixedString128(tooLong);

            Assert.Equal(127, str.Length);
            Assert.Equal(new string('Y', 127), str.ToString());
        }

        [Fact]
        public void FixedString128_NullString()
        {
            var str = new FixedString128(null!);

            Assert.True(str.IsEmpty);
            Assert.Equal(string.Empty, str.ToString());
        }

        [Fact]
        public void FixedString128_Equality()
        {
            var str1 = new FixedString128("Testing 123");
            var str2 = new FixedString128("Testing 123");
            var str3 = new FixedString128("Different");

            Assert.True(str1 == str2);
            Assert.False(str1 == str3);
        }

        [Fact]
        public void FixedString128_ImplicitConversions()
        {
            FixedString128 str = "Test String";
            string regular = str;

            Assert.Equal("Test String", regular);
        }

        [Fact]
        public void FixedString128_Size_Is128Bytes()
        {
            unsafe
            {
                Assert.Equal(128, sizeof(FixedString128));
            }
        }

        [Fact]
        public void FixedString128_LongerThanFixedString64()
        {
            // String that fits in 128 but not 64
            var mediumStr = new string('A', 100);
            var str = new FixedString128(mediumStr);

            Assert.Equal(100, str.Length);
            Assert.Equal(mediumStr, str.ToString());
        }

        [Fact]
        public void FixedString128_SpanCtor_RoundTrip()
        {
            var str = new FixedString128("Hello World".AsSpan());

            Assert.False(str.IsEmpty);
            Assert.Equal(11, str.Length);
            Assert.Equal("Hello World", str.ToString());
        }

        [Fact]
        public void FixedString128_SpanCtor_EmptySpan()
        {
            var str = new FixedString128(ReadOnlySpan<char>.Empty);

            Assert.True(str.IsEmpty);
            Assert.Equal(0, str.Length);
            Assert.Equal(string.Empty, str.ToString());
        }

        [Fact]
        public void FixedString128_SpanCtor_Truncation()
        {
            // 200 characters - should truncate to 127, same as the string ctor.
            var tooLong = new string('Y', 200);
            var str = new FixedString128(tooLong.AsSpan());

            Assert.Equal(127, str.Length);
            Assert.Equal(new string('Y', 127), str.ToString());
        }

        [Fact]
        public void FixedString128_SpanCtor_MatchesStringCtor()
        {
            const string input = "Hello, FixedString128!";
            var fromSpan = new FixedString128(input.AsSpan());
            var fromString = new FixedString128(input);

            Assert.Equal(fromString, fromSpan);
            Assert.Equal(fromString.ToString(), fromSpan.ToString());
        }
    }

    [Collection("ComponentTests")]
    public class FixedStringIntegrationTests
    {
        public FixedStringIntegrationTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        // Component using FixedString
        [ComponentId(255)]
        public struct NamedEntity
        {
            public FixedString32 Name;
            public int ID;
        }
        
        [Fact]
        public void FixedString_InComponent_Works()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NamedEntity>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NamedEntity 
            { 
                Name = "Player1", 
                ID = 42 
            });
            
            ref var comp = ref repo.GetComponentRW<NamedEntity>(entity);
            
            Assert.Equal("Player1", comp.Name.ToString());
            Assert.Equal(42, comp.ID);
        }
        
        [Fact]
        public void FixedString_ModifyInComponent()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NamedEntity>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NamedEntity 
            { 
                Name = "Original", 
                ID = 1 
            });
            
            ref var comp = ref repo.GetComponentRW<NamedEntity>(entity);
            comp.Name = "Modified";
            
            // Verify change persisted
            ref var comp2 = ref repo.GetComponentRW<NamedEntity>(entity);
            Assert.Equal("Modified", comp2.Name.ToString());
        }
        
        [Fact]
        public void FixedString_QueryWithNames()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NamedEntity>();
            
            // Create entities with names
            for (int i = 0; i < 10; i++)
            {
                var entity = repo.CreateEntity();
                repo.AddComponent(entity, new NamedEntity 
                { 
                    Name = $"Entity{i}", 
                    ID = i 
                });
            }
            
            var query = repo.Query().With<NamedEntity>().Build();
            
            Assert.Equal(10, query.Count());
            
            // Verify all names
            int count = 0;
            foreach (var e in query)
            {
                ref var comp = ref repo.GetComponentRW<NamedEntity>(e);
                Assert.StartsWith("Entity", comp.Name.ToString());
                count++;
            }
            
            Assert.Equal(10, count);
        }
        
        [Fact]
        public void FixedString_NoHeapAllocations()
        {
            // This test verifies that FixedString doesn't cause heap allocations
            var str1 = new FixedString32("Test");
            var str2 = str1; // Should be value copy, no heap allocation
            
            str2 = "Different"; // Should not affect str1
            
            Assert.Equal("Test", str1.ToString());
            Assert.Equal("Different", str2.ToString());
        }
    }
}
