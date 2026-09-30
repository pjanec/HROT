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
