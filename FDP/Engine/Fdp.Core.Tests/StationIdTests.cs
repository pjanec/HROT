using System;
using System.ComponentModel;
using System.Text.Json;
using Fdp.Core;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>⭐ <c>CE-11027</c> S12-1 (Q114 A, <c>docs/DESIGN_Player_Join.md</c> §12.2) — <see cref="StationId"/>: node in the low 16 bits, seat index in the high 16; conversions, JSON, type converter, limits.</summary>
    public class StationIdTests
    {
        [Fact]
        public void Of_PacksTheNodeLow_AndTheIndexHigh_AndANodeIsItsOwnStationZero()
        {
            var s = StationId.Of((NodeId)3, 2);
            Assert.Equal(3 | (2 << 16), s.Value);
            Assert.Equal((NodeId)3, s.Node);
            Assert.Equal(2, s.Index);
            StationId zero = StationId.Of((NodeId)3);              // a node's own station: index 0, said on purpose
            Assert.Equal(3, (int)zero);                         // a single-station node's id is unchanged
            Assert.Equal(0, zero.Index);
            Assert.Equal(StationId.Of((NodeId)3, 0), zero);
            Assert.Equal((StationId)(3 | (2 << 16)), s);        // explicit from the packed int
        }

        [Fact]
        public void TheIndexLimit_Is0To1024_AndANodeBeyond16BitsThrows()
        {
            Assert.Equal(1024, StationId.Of((NodeId)1, 1024).Index);
            Assert.Throws<ArgumentOutOfRangeException>(() => StationId.Of((NodeId)1, 1025));
            Assert.Throws<ArgumentOutOfRangeException>(() => StationId.Of((NodeId)1, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => StationId.Of((NodeId)0x10000, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => StationId.Of((NodeId)(-1), 0));
        }

        [Fact]
        public void ToString_ReadsStationN_OrStationNSlashIndex()
        {
            Assert.Equal("station 3", StationId.Of((NodeId)3, 0).ToString());
            Assert.Equal("station 3/2", StationId.Of((NodeId)3, 2).ToString());
        }

        [Fact]
        public void Json_IsAPlainNumber_AndRoundTrips()
        {
            var s = StationId.Of((NodeId)9, 5);
            string json = JsonSerializer.Serialize(s);
            Assert.Equal(s.Value.ToString(), json);
            Assert.Equal(s, JsonSerializer.Deserialize<StationId>(json));
            Assert.Equal(s, JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(new Holder { Who = s })).Who);
        }

        private sealed class Holder { public StationId Who { get; set; } }

        [Fact]
        public void TheTypeConverter_ReadsAStringOrAnInt_AndWritesBoth()
        {
            var tc = TypeDescriptor.GetConverter(typeof(StationId));
            var s = StationId.Of((NodeId)9, 5);
            Assert.Equal(s, tc.ConvertFrom(s.Value.ToString()));
            Assert.Equal(s, tc.ConvertFrom(s.Value));
            Assert.Equal(s.Value.ToString(), tc.ConvertTo(s, typeof(string)));
            Assert.Equal(s.Value, tc.ConvertTo(s, typeof(int)));
        }

        [Fact]
        public void EqualityAndOrdering_AreTheNumbers_AlsoAgainstARawInt()
        {
            var a = StationId.Of((NodeId)3, 1);
            Assert.True(a == StationId.Of((NodeId)3, 1));
            Assert.True(a != StationId.Of((NodeId)3, 2));
            Assert.True(a == a.Value);
            Assert.True(a.Value == a);
            Assert.True(a.CompareTo(StationId.Of((NodeId)3, 2)) < 0);
        }
    }
}
