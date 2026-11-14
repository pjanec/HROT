using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Behavior.Analyzers;
using Xunit;

namespace Fdp.Toolkit.Tests.FixedCollections
{
    /// <summary>
    /// FC-1b (Fixed Collections, Q#20 "G1 resolution") -- Roslyn driver tests for
    /// <see cref="CollectionOpsGenerator"/>. Mirrors <c>UtilityInputGeneratorTests</c>' harness
    /// (in-memory compilation + stub attributes matching the production Fdp.Core FQNs). The
    /// runtime behavior of the generated code is proven separately against the real
    /// <c>BpGenListDemoItemsOps</c> in Hrot.Blueprints.Tests -- these tests pin the generator's
    /// SHAPE: what it emits, what it refuses (FCOL diagnostics), and the hand-written-wins skip.
    /// </summary>
    public class CollectionOpsGeneratorTests
    {
        // Stubs mirroring the production Fdp.Core FQNs the generator + generated code reference.
        private const string CommonStubs = @"
namespace Fdp.Core
{
    public enum BlueprintCollectionOp { Add = 0, SetAt = 1, InsertAt = 2, RemoveAt = 3, Clear = 4, Resize = 5 }
    public enum CollectionAccess { ReadWrite = 0, ReadOnly = 1 }
    [System.Flags]
    public enum CollectionOps { None = 0, Add = 1, SetAt = 2, InsertAt = 4, RemoveAt = 8, Clear = 16, Resize = 32, All = 63 }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class BlueprintCollectionAttribute : System.Attribute
    {
        public BlueprintCollectionAttribute(System.Type componentType, string name) { }
    }
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class BlueprintCollectionItemAttribute : System.Attribute
    {
        public BlueprintCollectionItemAttribute(System.Type componentType, string name) { }
    }
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class BlueprintCollectionWriteAttribute : System.Attribute
    {
        public BlueprintCollectionWriteAttribute(System.Type componentType, string name, BlueprintCollectionOp op) { }
    }
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class BlueprintCollectionFieldAttribute : System.Attribute
    {
        public string CountField { get; }
        public CollectionAccess Access { get; set; }
        public CollectionOps Ops { get; set; }
        public BlueprintCollectionFieldAttribute(string countField) { CountField = countField; }
    }
}
";

        private const string ValidComponent = @"
using Fdp.Core;
using System.Runtime.CompilerServices;

namespace Demo
{
    public struct Roster
    {
        public const int Capacity = 4;
        [InlineArray(Capacity)] public struct Buffer { private int _e0; }

        public int Count;

        [BlueprintCollectionField(nameof(Count))]
        public Buffer Items;
    }
}
";

        private static (GeneratorDriverRunResult Result, Compilation Output) RunGenerator(string source)
        {
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var references = new[]
                {
                    typeof(object).Assembly.Location,                 // System.Private.CoreLib (net8: Span, InlineArrayAttribute)
                    Path.Combine(runtimeDir, "System.Runtime.dll"),
                    Path.Combine(runtimeDir, "netstandard.dll"),
                }
                .Where(File.Exists)
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToArray();

            var compilation = CSharpCompilation.Create(
                "GenTestAssembly",
                new[] { CSharpSyntaxTree.ParseText(CommonStubs), CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

            var driver = (CSharpGeneratorDriver)CSharpGeneratorDriver
                .Create(new CollectionOpsGenerator())
                .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

            return (driver.GetRunResult(), output);
        }

        private static string GeneratedSource(GeneratorDriverRunResult result) =>
            string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));

        // ---- happy path -------------------------------------------------------

        [Fact]
        public void ValidField_EmitsFullOpsClass_ThatCompiles()
        {
            var (result, output) = RunGenerator(ValidComponent);

            var src = GeneratedSource(result);
            Assert.Contains("public static class RosterItemsOps", src);
            Assert.Contains("namespace Demo", src);
            foreach (var member in new[] { "int Count(in", "int Item(in", "bool Add(ref", "bool SetAt(ref",
                                           "bool InsertAt(ref", "bool RemoveAt(ref", "void Clear(ref", "bool Resize(ref" })
                Assert.Contains(member, src);

            // The three load-bearing template rules survive generation:
            Assert.Contains("((Span<int>)c.Items)", src);                 // Span write-through
            Assert.Contains("s[count - 1] = default;", src);              // G6 vacated-slot zeroing
            Assert.Contains("__Clamp", src);                              // F2 defensive clamp
            Assert.DoesNotContain("c.Items[", src);                       // never the raw indexer

            // The generated code COMPILES against the real net8 BCL (Span conversions included).
            var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, "Generated code did not compile: "
                + string.Join("; ", errors.Select(e => e.ToString())));
        }

        [Fact]
        public void ReadOnlyAccess_EmitsReadPairOnly()
        {
            var (result, _) = RunGenerator(ValidComponent.Replace(
                "[BlueprintCollectionField(nameof(Count))]",
                "[BlueprintCollectionField(nameof(Count), Access = CollectionAccess.ReadOnly)]"));

            var src = GeneratedSource(result);
            Assert.Contains("int Count(in", src);
            Assert.Contains("int Item(in", src);
            Assert.DoesNotContain("bool Add(ref", src);
            Assert.DoesNotContain("void Clear(ref", src);
        }

        [Fact]
        public void OpsSubset_EmitsOnlySelectedWriteOps()
        {
            var (result, _) = RunGenerator(ValidComponent.Replace(
                "[BlueprintCollectionField(nameof(Count))]",
                "[BlueprintCollectionField(nameof(Count), Ops = CollectionOps.Add | CollectionOps.Clear)]"));

            var src = GeneratedSource(result);
            Assert.Contains("bool Add(ref", src);
            Assert.Contains("void Clear(ref", src);
            Assert.DoesNotContain("bool SetAt(ref", src);
            Assert.DoesNotContain("bool InsertAt(ref", src);
            Assert.DoesNotContain("bool RemoveAt(ref", src);
            Assert.DoesNotContain("bool Resize(ref", src);
        }

        // ---- diagnostics ------------------------------------------------------

        [Fact]
        public void MissingCountField_FCOL001_NothingEmitted()
        {
            var (result, _) = RunGenerator(ValidComponent.Replace("nameof(Count)", "\"NoSuchField\""));
            Assert.Contains(result.Diagnostics, d => d.Id == "FCOL001");
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void NonInlineArrayFieldType_FCOL002_NothingEmitted()
        {
            const string source = @"
using Fdp.Core;
namespace Demo
{
    public struct Roster
    {
        public int Count;
        [BlueprintCollectionField(nameof(Count))]
        public int Items;   // not an [InlineArray] struct
    }
}
";
            var (result, _) = RunGenerator(source);
            Assert.Contains(result.Diagnostics, d => d.Id == "FCOL002");
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void ManagedElementType_FCOL003_NothingEmitted()
        {
            const string source = @"
using Fdp.Core;
using System.Runtime.CompilerServices;
namespace Demo
{
    public struct Roster
    {
        [InlineArray(4)] public struct Buffer { private string _e0; }
        public int Count;
        [BlueprintCollectionField(nameof(Count))]
        public Buffer Items;
    }
}
";
            var (result, _) = RunGenerator(source);
            Assert.Contains(result.Diagnostics, d => d.Id == "FCOL003");
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void ClassComponent_FCOL004_NothingEmitted()
        {
            const string source = @"
using Fdp.Core;
using System.Runtime.CompilerServices;
namespace Demo
{
    public class Roster
    {
        [InlineArray(4)] public struct Buffer { private int _e0; }
        public int Count;
        [BlueprintCollectionField(nameof(Count))]
        public Buffer Items;
    }
}
";
            var (result, _) = RunGenerator(source);
            Assert.Contains(result.Diagnostics, d => d.Id == "FCOL004");
            Assert.Empty(result.GeneratedTrees);
        }

        // ---- hand-written wins ------------------------------------------------

        [Fact]
        public void HandWrittenAccessorForSameCollection_SkipsGeneration_Silently()
        {
            const string handWritten = @"
using Fdp.Core;
namespace Demo
{
    public static class RosterOpsByHand
    {
        [BlueprintCollection(typeof(Roster), ""Items"")]
        public static int Count(in Roster r) => r.Count;
    }
}
";
            var (result, _) = RunGenerator(ValidComponent + handWritten);
            Assert.Empty(result.GeneratedTrees);      // generator yields to the hand-written class
            Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void HandWrittenAccessorForDifferentCollection_DoesNotBlockGeneration()
        {
            const string handWritten = @"
using Fdp.Core;
namespace Demo
{
    public static class RosterOpsByHand
    {
        [BlueprintCollection(typeof(Roster), ""SomethingElse"")]
        public static int Count(in Roster r) => r.Count;
    }
}
";
            var (result, _) = RunGenerator(ValidComponent + handWritten);
            Assert.Single(result.GeneratedTrees);
        }
    }
}
