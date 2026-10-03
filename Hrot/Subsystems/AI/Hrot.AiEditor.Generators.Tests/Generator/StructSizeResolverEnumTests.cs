using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Generator
{
    /// <summary>
    /// ⭐ <c>CE-473</c> — the blueprint compiler's field-size oracle sizes an ENUM by its underlying type.
    /// </summary>
    /// <remarks>
    /// The compiler's <c>global::</c> arm guesses 4 bytes for any project type; <c>Stage4_TypeResolve.WithOracleSize</c>
    /// replaces that guess with the oracle's answer (<see cref="StructSizeResolver.MakeFieldSizeDelegate"/>), and
    /// without an answer the emitter keeps <c>Sequential</c> layout. ⚠ A <c>ulong</c> enum sized as 4 would let the next
    /// baked <c>[FieldOffset]</c> overlap it; a <c>byte</c> enum sized as 4 only wastes padding. This rail pins both.
    /// 📄 <c>Architect_Question_78</c> §7 row 8.
    /// </remarks>
    public sealed class StructSizeResolverEnumTests
    {
        private const string Source = @"
namespace Probe
{
    public enum ByteEnum  : byte  { A, B }
    public enum ShortEnum : short { A }
    public enum IntEnum            { A }
    public enum ULongEnum : ulong { A = 1UL << 40 }
}";

        private static System.Func<string, int?> Oracle()
        {
            var compilation = CSharpCompilation.Create(
                "EnumSizeProbe",
                new[] { CSharpSyntaxTree.ParseText(Source) },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return StructSizeResolver.MakeFieldSizeDelegate(compilation);
        }

        // ── ⭐⭐ CE-2027 — the ONE struct-layout algorithm, checked against the CLR itself ─────────────────────────────
        //
        // The same source is compiled twice: Roslyn symbols for the algorithm, and a real assembly whose types the runtime
        // lays out. ⭐ The runtime's answer (Unsafe.SizeOf / Marshal.OffsetOf) is the truth every copy claimed to mirror.
        private const string LayoutSource = @"
using System.Runtime.InteropServices;
namespace Layout
{
    public enum ByteEnum : byte { A }
    public struct V3 { public float X, Y, Z; }                                  // 12 bytes, 4-aligned
    public struct B3 { public byte A, B, C; }                                   // 3 bytes, 1-aligned
    public struct IntV3 { public int A; public V3 V; }                         // V3 aligns to 4, not 8
    public struct ByteV3 { public byte A; public V3 V; }
    public struct IntB3 { public int A; public B3 B; }
    public struct LongByte { public long A; public byte B; }
    public struct ByteEnumInt { public ByteEnum E; public int I; }
    public struct BoolInt { public bool A; public int B; }
    public struct CharByte { public char A; public byte B; }
    [StructLayout(LayoutKind.Explicit)] public struct Ex { [FieldOffset(0)] public int A; [FieldOffset(4)] public byte B; }
    public struct ByteEx { public byte A; public Ex E; }
    public struct V3Long { public V3 V; public long L; }
    public struct Empty { }
    public struct ByteEmpty { public byte A; public Empty E; public byte B; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct Packed1 { public byte A; public int B; public long C; }
    [StructLayout(LayoutKind.Sequential, Size = 32)] public struct Sized32 { public int A; }
    public unsafe struct Fixed { public byte A; public fixed int B[3]; public short C; }
    public struct IntVector3 { public int A; public System.Numerics.Vector3 V; }
}";

        private static CSharpCompilation Compilation() => CSharpCompilation.Create(
                "LayoutProbe",
                new[] { CSharpSyntaxTree.ParseText(LayoutSource) },
                new[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Runtime.InteropServices.StructLayoutAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Numerics.Vector3).Assembly.Location),
                    MetadataReference.CreateFromFile(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
                },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        private static (System.Func<string, int?> Size, System.Reflection.Assembly Real) Layouts()
        {
            var compilation = Compilation();
            using var pe = new System.IO.MemoryStream();
            var emitted = compilation.Emit(pe);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            return (StructSizeResolver.MakeDelegate(compilation), System.Reflection.Assembly.Load(pe.ToArray()));
        }

        private static int ClrSizeOf(System.Type t)
            => (int)typeof(System.Runtime.CompilerServices.Unsafe).GetMethod("SizeOf")!.MakeGenericMethod(t).Invoke(null, null)!;

        [Theory]
        [InlineData("Layout.V3")] [InlineData("Layout.B3")] [InlineData("Layout.IntV3")] [InlineData("Layout.ByteV3")]
        [InlineData("Layout.IntB3")] [InlineData("Layout.LongByte")] [InlineData("Layout.ByteEnumInt")] [InlineData("Layout.BoolInt")]
        [InlineData("Layout.CharByte")] [InlineData("Layout.Ex")] [InlineData("Layout.ByteEx")] [InlineData("Layout.V3Long")]
        [InlineData("Layout.ByteEmpty")] [InlineData("Layout.Packed1")] [InlineData("Layout.Sized32")] [InlineData("Layout.Fixed")]
        [InlineData("Layout.IntVector3")]
        public void CE2027_TheStructSize_IsTheClrsManagedSize(string type)
        {
            var (size, real) = Layouts();
            Assert.Equal(ClrSizeOf(real.GetType(type, throwOnError: true)!), size(type));
        }

        [Theory]
        [InlineData("Layout.IntV3", "V")] [InlineData("Layout.ByteV3", "V")] [InlineData("Layout.IntB3", "B")]
        [InlineData("Layout.LongByte", "B")] [InlineData("Layout.ByteEx", "E")] [InlineData("Layout.V3Long", "L")]
        [InlineData("Layout.Packed1", "C")] [InlineData("Layout.Fixed", "C")] [InlineData("Layout.IntVector3", "V")]
        public void CE2027_AFieldOffset_IsTheClrsOffset(string type, string field)
        {
            var (_, real) = Layouts();
            var compilation = Compilation();
            var symbol = compilation.GetTypeByMetadataName(type)!;
            int expected = (int)System.Runtime.InteropServices.Marshal.OffsetOf(real.GetType(type, throwOnError: true)!, field);
            Assert.Equal(expected, Fdp.Toolkit.Behavior.Shared.RoslynStructLayout.FieldOffset(symbol, field, out _));
        }

        /// <summary>
        /// ⭐ <c>CE-2027</c> — the SHIPPED types whose computed size moved when the algorithm became the CLR's (measured by diffing
        /// the generated output of <c>Hrot.AI.Behaviors</c>): sized from the real compilation's symbols, checked against the loaded
        /// types. 📌 <c>PlatoonHillAttackParams</c> was 56 (an 8-byte <c>Entity</c> guessed 8-aligned) where its own source says 52.
        /// </summary>
        [Theory]
        [InlineData(typeof(global::Hrot.AI.Behaviors.Brains.PlatoonHillAttackParams))]
        [InlineData(typeof(global::Hrot.AI.Behaviors.Brains.HillAttackMutableState))]   // fixed buffers: unsizeable before
        [InlineData(typeof(global::Hrot.AI.Behaviors.Brains.HillAttackRunner))]
        [InlineData(typeof(global::Fdp.Toolkit.Behavior.Params.PickableGeoPoint))]
        [InlineData(typeof(global::Fdp.Core.Entity))]
        public void CE2027_AShippedStruct_IsSizedAsTheClrLaysItOut(System.Type type)
        {
            var references = new System.Collections.Generic.List<MetadataReference>();
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                    references.Add(MetadataReference.CreateFromFile(asm.Location));
            var compilation = CSharpCompilation.Create("ShippedProbe", System.Array.Empty<SyntaxTree>(), references);
            Assert.Equal(ClrSizeOf(type), StructSizeResolver.Resolve(type.FullName!, compilation));
        }

        [Theory]
        [InlineData("global::Probe.ByteEnum", 1)]
        [InlineData("global::Probe.ShortEnum", 2)]
        [InlineData("global::Probe.IntEnum", 4)]
        [InlineData("global::Probe.ULongEnum", 8)]
        [InlineData("Probe.ULongEnum", 8)]
        public void AnEnumIsSizedByItsUnderlyingType(string typeId, int expected)
            => Assert.Equal(expected, Oracle()(typeId));
    }
}
