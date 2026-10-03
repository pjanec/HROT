using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Xunit;

namespace Fdp.Core.Tests
{
    /// <summary>
    /// ⭐ <c>CE-2030</c>/<c>CE-2041</c>/<c>CE-2043</c> — <see cref="TypeLayout"/> answers in the MANAGED layout: the bytes a
    /// blackboard, component or generated struct actually occupies. 📄 <c>DESIGN_Unified_Behaviour_Run.md</c> S8e/S8h.
    /// Every fixture puts a <c>bool</c> first, because that is exactly where the interop layout (Marshal) disagrees.
    /// </summary>
    public class TypeLayoutTests
    {
#pragma warning disable CS0649   // layout-only fixtures
        private struct FlagsThenFloat { public bool A; public bool B; public float C; }
        private struct FlagThenVector { public bool Hit; public Vector3 Point; public float Distance; }
#pragma warning restore CS0649

        [Fact]
        public void OffsetOf_IsTheManagedOffset_NotTheInteropOne()
        {
            Assert.Equal(0, TypeLayout.OffsetOf(typeof(FlagsThenFloat), "A"));
            Assert.Equal(1, TypeLayout.OffsetOf(typeof(FlagsThenFloat), "B"));
            Assert.Equal(4, TypeLayout.OffsetOf(typeof(FlagsThenFloat), "C"));          // Marshal.OffsetOf: 8
            // a lone bool before a 4-aligned field: padding makes both layouts agree — the helper must not invent a difference
            Assert.Equal(4, TypeLayout.OffsetOf(typeof(FlagThenVector), "Point"));
            Assert.Equal(16, TypeLayout.OffsetOf(typeof(FlagThenVector), "Distance"));
        }

        [Fact]
        public void OffsetOf_AgreesWithTheAddressOfTheField()
        {
            var v = new FlagsThenFloat();
            int byAddress = (int)Unsafe.ByteOffset(ref Unsafe.As<FlagsThenFloat, byte>(ref v), ref Unsafe.As<float, byte>(ref v.C));
            Assert.Equal(byAddress, TypeLayout.OffsetOf(typeof(FlagsThenFloat), "C"));
        }

        [Fact]
        public void SizeOf_AndRead_UseTheManagedLayout()
        {
            Assert.Equal(8, TypeLayout.SizeOf(typeof(FlagsThenFloat)));                 // Marshal.SizeOf: 12
            var bytes = new byte[8];
            bytes[0] = 1;
            BitConverter.GetBytes(2.5f).CopyTo(bytes, 4);
            var v = (FlagsThenFloat)TypeLayout.Read(bytes, 0, typeof(FlagsThenFloat));
            Assert.True(v.A);
            Assert.False(v.B);
            Assert.Equal(2.5f, v.C);
        }

        [Fact]
        public void OffsetOf_RefusesANonStructOrAMissingField()
        {
            Assert.Throws<ArgumentException>(() => TypeLayout.OffsetOf(typeof(string), "Length"));
            Assert.Throws<ArgumentException>(() => TypeLayout.OffsetOf(typeof(FlagsThenFloat), "Nope"));
        }

        [Fact]
        public void Read_RefusesATypeHoldingReferences()
            => Assert.Throws<ArgumentException>(() => TypeLayout.Read(new byte[16], 0, typeof(ValueTuple<string>)));
    }
}
