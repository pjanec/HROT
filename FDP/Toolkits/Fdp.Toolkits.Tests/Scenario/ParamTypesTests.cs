using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>⭐ CE-4210 MA-3 — the ONE table from a param's TypeId to a CLR type: both spellings read, each list's own written, Fits decides what a bind menu offers.
    /// 🔴 Red-proof: drop one row of the table ⇒ the round-trip rail names it.</summary>
    public class ParamTypesTests
    {
        public static TheoryData<string, System.Type> ShortIds => new()
        {
            { "EntityLink", typeof(EntityLink) }, { "PointLink", typeof(PointLink) }, { "AreaLink", typeof(AreaLink) },
            { "Bearing", typeof(Bearing) }, { "WorldRotation", typeof(WorldRotation) },
            { "float", typeof(float) }, { "int", typeof(int) }, { "bool", typeof(bool) }, { "string", typeof(string) }, { "double", typeof(double) },
            { "ForceId", typeof(ForceId) },
        };

        [Theory, MemberData(nameof(ShortIds))]
        public void EveryShortId_RoundTrips_AndSpellsItselfBack(string id, System.Type type)
        {
            Assert.True(ParamTypes.TryResolve(id, out var resolved));
            Assert.Equal(type, resolved);
            Assert.Equal(id, ParamTypes.IdOf(type, ParamTypeSpelling.Short));          // a header's own list writes the short name
            Assert.True(ParamTypes.TryResolve(ParamTypes.IdOf(type, ParamTypeSpelling.Clr), out var viaClr));
            Assert.Equal(type, viaClr);                                                // and a .params.json's CLR name resolves to the same type
        }

        [Fact]
        public void TheClrSpellingsOfAParamsJson_Resolve_AndAreWrittenBack()
        {
            Assert.True(ParamTypes.TryResolve("System.Single", out var f)); Assert.Equal(typeof(float), f);
            Assert.True(ParamTypes.TryResolve("System.Numerics.Vector3", out var v)); Assert.Equal(typeof(Vector3), v);   // T41Params.Home
            Assert.True(ParamTypes.TryResolve("System.Int32", out var i)); Assert.Equal(typeof(int), i);
            Assert.True(ParamTypes.TryResolve("System.Boolean", out var b)); Assert.Equal(typeof(bool), b);
            Assert.Equal("System.Single", ParamTypes.IdOf(typeof(float), ParamTypeSpelling.Clr));
            Assert.Equal("System.Numerics.Vector3", ParamTypes.IdOf(typeof(Vector3), ParamTypeSpelling.Clr));
            Assert.True(ParamTypes.TryResolve("float", out var lower)); Assert.True(ParamTypes.TryResolve("FLOAT", out var upper));
            Assert.Equal(lower, upper);   // case-insensitive
        }

        [Fact]
        public void Fits_HoldsForTheSameType_AndNullableOfIt_AndFailsForAnyOtherOrUnknown()
        {
            Assert.True(ParamTypes.Fits("AreaLink", typeof(AreaLink)));
            Assert.True(ParamTypes.Fits("float", typeof(float)));
            Assert.True(ParamTypes.Fits("System.Single", typeof(float)));      // either spelling
            Assert.True(ParamTypes.Fits("int", typeof(int?)));
            Assert.True(ParamTypes.Fits("ForceId", typeof(ForceId)));
            Assert.False(ParamTypes.Fits("AreaLink", typeof(PointLink)));
            Assert.False(ParamTypes.Fits("float", typeof(int)));
            Assert.False(ParamTypes.Fits("ForceId", typeof(string)));
            Assert.False(ParamTypes.Fits("NoSuchType", typeof(float)));
            Assert.False(ParamTypes.Fits(null, typeof(float)));
            Assert.False(ParamTypes.Fits("float", null));
        }

        [Fact]
        public void AnUnknownId_ResolvesToNothing_NeverThrows_AndANewEnumIsRegisteredOnce()
        {
            Assert.False(ParamTypes.TryResolve("", out _));
            Assert.False(ParamTypes.TryResolve(null, out _));
            Assert.False(ParamTypes.TryResolve("Nope.Nothing", out _));
            Assert.Null(ParamTypes.IdOf(typeof(System.Guid)));
            Assert.Null(ParamTypes.IdOf(null));

            Assert.False(ParamTypes.TryResolve(nameof(Stance), out _));          // an enum is a param type only when registered
            ParamTypes.RegisterEnum(typeof(Stance)); ParamTypes.RegisterEnum(typeof(Stance)); ParamTypes.RegisterEnum(typeof(int));   // idempotent; a non-enum is ignored
            Assert.True(ParamTypes.TryResolve(nameof(Stance), out var e)); Assert.Equal(typeof(Stance), e);
            Assert.Equal(typeof(Stance).FullName, ParamTypes.IdOf(typeof(Stance), ParamTypeSpelling.Clr));
            Assert.False(ParamTypes.TryResolve("Int32", out _));
        }

        private enum Stance { Stand, Crouch }
    }
}
