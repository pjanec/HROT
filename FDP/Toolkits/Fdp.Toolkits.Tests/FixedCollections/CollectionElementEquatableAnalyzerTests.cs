using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fdp.Toolkit.Behavior.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Fdp.Toolkit.Tests.FixedCollections
{
    /// <summary>
    /// FCOL005 (Q#21 wrap-up) — <see cref="CollectionElementEquatableAnalyzer"/>: a
    /// blueprint-searchable collection whose element is a user struct without
    /// <c>IEquatable&lt;T&gt;</c> warns (Contains/Find would box per comparison);
    /// primitives, enums, and equatable structs stay clean, on BOTH declaration sites
    /// ([BlueprintCollectionItem] accessors and [BlueprintCollectionField] fields).
    /// </summary>
    public class CollectionElementEquatableAnalyzerTests
    {
        // Same attribute stubs as CollectionOpsGeneratorTests (production Fdp.Core FQNs).
        private const string Stubs = @"
namespace Fdp.Core
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class BlueprintCollectionItemAttribute : System.Attribute
    {
        public BlueprintCollectionItemAttribute(System.Type componentType, string name) { }
    }
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class BlueprintCollectionFieldAttribute : System.Attribute
    {
        public BlueprintCollectionFieldAttribute(string countField) { }
    }
}
";

        private static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
        {
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var references = new[]
                {
                    typeof(object).Assembly.Location,
                    Path.Combine(runtimeDir, "System.Runtime.dll"),
                    Path.Combine(runtimeDir, "netstandard.dll"),
                    Path.Combine(runtimeDir, "System.Numerics.Vectors.dll"),
                }
                .Where(File.Exists)
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToArray();

            var compilation = CSharpCompilation.Create(
                "EqAnalyzerTestAssembly",
                new[] { CSharpSyntaxTree.ParseText(Stubs), CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

            var withAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new CollectionElementEquatableAnalyzer()));
            return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        [Fact]
        public async Task ItemAccessor_NonEquatableStructElement_WarnsFCOL005()
        {
            var diags = await Analyze(@"
using Fdp.Core;
namespace Demo
{
    public struct Waypoint { public float X; public float R; }
    public struct Comp { }
    public static class Ops
    {
        [BlueprintCollectionItem(typeof(Comp), ""Items"")]
        public static Waypoint Item(in Comp c, int i) => default;
    }
}");
            var d = Assert.Single(diags, x => x.Id == "FCOL005");
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Contains("Waypoint", d.GetMessage());
            Assert.Contains("IEquatable<Waypoint>", d.GetMessage());
        }

        [Fact]
        public async Task ItemAccessor_EquatableStructElement_Clean()
        {
            var diags = await Analyze(@"
using Fdp.Core;
namespace Demo
{
    public struct Waypoint : System.IEquatable<Waypoint>
    {
        public float X;
        public bool Equals(Waypoint other) => X == other.X;
    }
    public struct Comp { }
    public static class Ops
    {
        [BlueprintCollectionItem(typeof(Comp), ""Items"")]
        public static Waypoint Item(in Comp c, int i) => default;
    }
}");
            Assert.DoesNotContain(diags, x => x.Id == "FCOL005");
        }

        [Fact]
        public async Task ItemAccessor_PrimitiveAndEnumElements_Clean()
        {
            var diags = await Analyze(@"
using Fdp.Core;
namespace Demo
{
    public enum Kind { A, B }
    public struct Comp { }
    public static class Ops
    {
        [BlueprintCollectionItem(typeof(Comp), ""Ints"")]
        public static int IntItem(in Comp c, int i) => 0;

        [BlueprintCollectionItem(typeof(Comp), ""Kinds"")]
        public static Kind KindItem(in Comp c, int i) => default;
    }
}");
            Assert.DoesNotContain(diags, x => x.Id == "FCOL005");
        }

        [Fact]
        public async Task CollectionField_NonEquatableStructElement_WarnsFCOL005()
        {
            var diags = await Analyze(@"
using Fdp.Core;
using System.Runtime.CompilerServices;
namespace Demo
{
    public struct Waypoint { public float X; }
    public struct Roster
    {
        [InlineArray(4)] public struct Buffer { private Waypoint _e0; }
        public int Count;
        [BlueprintCollectionField(nameof(Count))]
        public Buffer Items;
    }
}");
            var d = Assert.Single(diags, x => x.Id == "FCOL005");
            Assert.Contains("Waypoint", d.GetMessage());
        }

        [Fact]
        public async Task CollectionField_IntElement_Clean()
        {
            var diags = await Analyze(@"
using Fdp.Core;
using System.Runtime.CompilerServices;
namespace Demo
{
    public struct Roster
    {
        [InlineArray(4)] public struct Buffer { private int _e0; }
        public int Count;
        [BlueprintCollectionField(nameof(Count))]
        public Buffer Items;
    }
}");
            Assert.DoesNotContain(diags, x => x.Id == "FCOL005");
        }

        [Fact]
        public async Task UnattributedSites_NeverAnalyzed()
        {
            // The same non-equatable struct with NO collection attributes — no warning
            // (the analyzer keys strictly off the two declaration-site attributes).
            var diags = await Analyze(@"
namespace Demo
{
    public struct Waypoint { public float X; }
    public static class Ops
    {
        public static Waypoint Item(int i) => default;
    }
}");
            Assert.DoesNotContain(diags, x => x.Id == "FCOL005");
        }
    }
}
