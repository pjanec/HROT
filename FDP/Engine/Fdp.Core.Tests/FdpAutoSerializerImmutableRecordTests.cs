using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Fdp.Core.FlightRecorder;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ CE-4046 step 1 (coordinator handoff "Replay Recording Fixes" ①; docs/DESIGN_Replay_Census_H0.md §2) — the recorder's serializer
    /// round-trips the shapes an IMMUTABLE managed component takes: a record with <c>init</c> members, a positional record (no parameterless
    /// constructor), and read-only collection members (<c>IReadOnlyList</c>, <c>ImmutableArray</c>, <c>IReadOnlyDictionary</c>).
    /// 🔒 User, 2026-10-25: <i>"The managed components was meant to be csharp records"</i>.
    /// </summary>
    public class FdpAutoSerializerImmutableRecordTests
    {
        public sealed record InitRecord
        {
            public int Id { get; init; }
            public string? Name { get; init; }
            public double Speed { get; init; }
        }

        public sealed record PositionalRecord(int Id, string? Name, double Speed);

        public sealed record ReadOnlyListRecord
        {
            public int Id { get; init; }
            public IReadOnlyList<int> Numbers { get; init; } = System.Array.Empty<int>();
            public IReadOnlyList<string> Names { get; init; } = System.Array.Empty<string>();
        }

        public sealed record ImmutableArrayRecord
        {
            public ImmutableArray<float> Points { get; init; } = ImmutableArray<float>.Empty;
            public ImmutableArray<long> Ids { get; init; }
        }

        public sealed record ReadOnlyDictionaryRecord
        {
            public IReadOnlyDictionary<long, int> Owners { get; init; } = new Dictionary<long, int>();
        }

        public sealed record Inner(int A, float B);

        public sealed record NestedRecord
        {
            public IReadOnlyList<Inner> Items { get; init; } = System.Array.Empty<Inner>();
            public Inner? One { get; init; }
        }

        private static T RoundTrip<T>(T value)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                FdpAutoSerializer.Serialize(value, w);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            return FdpAutoSerializer.Deserialize<T>(r);
        }

        [Fact]
        public void AnInitOnlyRecord_RoundTrips()
        {
            var v = new InitRecord { Id = 7, Name = "Sentry", Speed = 2.5 };
            Assert.Equal(v, RoundTrip(v));
        }

        [Fact]
        public void APositionalRecord_RoundTrips_WithoutAParameterlessConstructor()
        {
            var v = new PositionalRecord(7, "Sentry", 2.5);
            Assert.Equal(v, RoundTrip(v));
        }

        [Fact]
        public void ReadOnlyListMembers_RoundTrip()
        {
            var v = new ReadOnlyListRecord { Id = 3, Numbers = new[] { 1, 2, 3 }, Names = new List<string> { "a", "b" } };
            var back = RoundTrip(v);
            Assert.Equal(3, back.Id);
            Assert.Equal(new[] { 1, 2, 3 }, back.Numbers);
            Assert.Equal(new[] { "a", "b" }, back.Names);
        }

        [Fact]
        public void ImmutableArrayMembers_RoundTrip_AndADefaultOneStaysDefault()
        {
            var v = new ImmutableArrayRecord { Points = ImmutableArray.Create(1f, 2f, 3f) };
            var back = RoundTrip(v);
            Assert.Equal(new[] { 1f, 2f, 3f }, back.Points.ToArray());
            Assert.True(back.Ids.IsDefault);
        }

        [Fact]
        public void ReadOnlyDictionaryMembers_RoundTrip()
        {
            var v = new ReadOnlyDictionaryRecord { Owners = new Dictionary<long, int> { [10] = 1, [11] = 2 } };
            var back = RoundTrip(v);
            Assert.Equal(2, back.Owners.Count);
            Assert.Equal(2, back.Owners[11]);
        }

        [Fact]
        public void NestedRecords_RoundTrip()
        {
            var v = new NestedRecord { Items = new[] { new Inner(1, 1.5f), new Inner(2, 2.5f) }, One = new Inner(9, 9f) };
            var back = RoundTrip(v);
            Assert.Equal(v.Items, back.Items);
            Assert.Equal(v.One, back.One);
        }

        [Fact]
        public void ARecord_IsClonedByReference()
        {
            var v = new ReadOnlyListRecord { Id = 1, Numbers = new[] { 1 } };
            Assert.Same(v, FdpAutoSerializer.DeepClone(v));
        }
    }
}
