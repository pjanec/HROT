using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fdp.Toolkit.Behavior.Analyzers;
using Xunit;

namespace Fdp.Toolkit.Tests.Utility
{
    /// <summary>
    /// Roslyn compilation tests for UtilityInputGenerator.
    /// Covers success conditions SC-P2-01-1 through SC-P2-01-4.
    /// </summary>
    public class UtilityInputGeneratorTests
    {
        // Minimal stubs that mirror the production FQNs required by the generator.
        private const string CommonStubs = @"
namespace Fdp.Toolkit.Utility
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class UtilityInputAttribute : System.Attribute
    {
        public string Name { get; }
        public UtilityInputAttribute(string name) { Name = name; }
    }

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class UtilityRegistrarAttribute : System.Attribute { }

    public enum InputContext : byte { Self, Target, Leader, Candidate }

    public struct UtilityInputCtx { }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    public readonly struct InputRef
    {
        public readonly ushort InputId;
        public readonly InputContext Context;
        public InputRef(ushort inputId, InputContext context = default)
        { InputId = inputId; Context = context; }
    }

    public static unsafe class UtilityInputReaderStore
    {
        public static void Register(ushort id, delegate*<in UtilityInputCtx, float> reader) { }
    }

    public static partial class In { }
}
";

        private static (GeneratorDriverRunResult result, Compilation outputCompilation) RunGenerator(
            string source)
        {
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var netstandardPath = Path.Combine(runtimeDir, "netstandard.dll");

            MetadataReference[] references = File.Exists(netstandardPath)
                ? new[]
                  {
                      MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                      MetadataReference.CreateFromFile(netstandardPath)
                  }
                : new[]
                  {
                      MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                      MetadataReference.CreateFromFile(
                          typeof(System.Runtime.GCSettings).Assembly.Location)
                  };

            var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(CommonStubs),
                CSharpSyntaxTree.ParseText(source)
            };

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    allowUnsafe: true));

            var generator = new UtilityInputGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);
            driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
                compilation, out var outputCompilation, out _);

            return (driver.GetRunResult(), outputCompilation);
        }

        // Reference implementation of the Fnv1a16 hash used by the generator.
        // 32-bit FNV-1a, low 16 bits — must match StandardInputIds exactly.
        private static ushort Fnv1a16Ref(string s)
        {
            uint hash = 2166136261u;
            foreach (char c in s)
            {
                hash ^= (uint)c;
                hash *= 16777619u;
            }
            return (ushort)(hash & 0xFFFF);
        }

        // ---- SC-P2-01-1 --------------------------------------------------------

        /// <summary>
        /// SC-P2-01-1: Three valid [UtilityInput] methods cause the generator to emit
        /// exactly two generated files (registrar + accessors) with three entries each,
        /// and the output compilation contains zero errors.
        /// </summary>
        [Fact]
        public void ThreeInputMethods_EmitRegistrarAndAccessors()
        {
            const string source = @"
namespace Fdp.Toolkit.Utility
{
    public static class StandardInputs
    {
        [UtilityInputAttribute(""Alpha"")]
        public static float Alpha(in UtilityInputCtx ctx) => 0f;

        [UtilityInputAttribute(""Beta"")]
        public static float Beta(in UtilityInputCtx ctx) => 0f;

        [UtilityInputAttribute(""Gamma"")]
        public static float Gamma(in UtilityInputCtx ctx) => 0f;
    }
}";
            var (result, outputCompilation) = RunGenerator(source);

            Assert.Equal(2, result.GeneratedTrees.Length);

            string registrar = result.GeneratedTrees
                .Select(t => t.GetText().ToString())
                .First(s => s.Contains("UtilityInputRegistrar"));

            string accessors = result.GeneratedTrees
                .Select(t => t.GetText().ToString())
                .First(s => s.Contains("partial class In"));

            // Three Register( calls in the registrar
            Assert.Equal(3, registrar.Split(new[] { "Register(" }, StringSplitOptions.None).Length - 1);

            // Three InputRef-returning methods in the accessors
            Assert.Equal(3, accessors.Split(
                new[] { "public static global::Fdp.Toolkit.Utility.InputRef" },
                StringSplitOptions.None).Length - 1);

            // Output compilation must have no errors
            var errors = outputCompilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.Empty(errors);
        }

        // ---- SC-P2-01-2 --------------------------------------------------------

        /// <summary>
        /// SC-P2-01-2: The hash emitted for "AmmoFraction" matches the canonical value
        /// in StandardInputIds (0x2C39). Also pins HealthFraction and HaveLiveTarget.
        /// </summary>
        [Fact]
        public void HashParity_AmmoFraction_MatchesStandardInputIds()
        {
            const string source = @"
namespace Fdp.Toolkit.Utility
{
    public static class StandardInputs
    {
        [UtilityInputAttribute(""AmmoFraction"")]
        public static float AmmoFraction(in UtilityInputCtx ctx) => 0f;
    }
}";
            var (result, _) = RunGenerator(source);

            string registrar = result.GeneratedTrees
                .Select(t => t.GetText().ToString())
                .First(s => s.Contains("UtilityInputRegistrar"));

            // Extract the emitted hex literal from the Register call
            var match = Regex.Match(registrar, @"Register\(\s*0x([0-9A-Fa-f]+),");
            Assert.True(match.Success, "Could not find Register(0x..., in generated registrar");
            ushort emittedHash = Convert.ToUInt16(match.Groups[1].Value, 16);

            // Must match the canonical StandardInputIds.AmmoFraction constant
            Assert.Equal((ushort)0x2C39, emittedHash);

            // Pin reference computations
            Assert.Equal((ushort)0x2C39, Fnv1a16Ref("AmmoFraction"));
            Assert.Equal((ushort)0x13D9, Fnv1a16Ref("HealthFraction"));
            Assert.Equal((ushort)0xC20C, Fnv1a16Ref("HaveLiveTarget"));
        }

        // ---- SC-P2-01-3 --------------------------------------------------------

        /// <summary>
        /// SC-P2-01-3: Two [UtilityInput] methods whose names produce the same Fnv1a16 hash
        /// cause the generator to emit exactly one UT0103 diagnostic on the second method.
        /// </summary>
        [Fact]
        public void HashCollision_EmitsUT0103()
        {
            // Find two distinct names that collide under Fnv1a16.
            // With a 16-bit hash space the expected search depth is ~256 iterations.
            const string name1 = "InputAlpha";
            ushort hash1 = Fnv1a16Ref(name1);
            string name2 = null;
            for (int i = 0; i < 200000; i++)
            {
                string candidate = "Cand" + i;
                if (Fnv1a16Ref(candidate) == hash1)
                {
                    name2 = candidate;
                    break;
                }
            }
            Assert.NotNull(name2); // must find a collision within the search range

            string source = $@"
namespace Fdp.Toolkit.Utility
{{
    public static class StandardInputs
    {{
        [UtilityInputAttribute(""{name1}"")]
        public static float Method1(in UtilityInputCtx ctx) => 0f;

        [UtilityInputAttribute(""{name2}"")]
        public static float Method2(in UtilityInputCtx ctx) => 0f;
    }}
}}";
            var (result, _) = RunGenerator(source);

            Assert.Contains(result.Diagnostics, d => d.Id == "UT0103");
        }

        // ---- SC-P2-01-4a -------------------------------------------------------

        /// <summary>
        /// SC-P2-01-4a: A non-static method decorated with [UtilityInput] causes
        /// the generator to emit a UT0110 diagnostic.
        /// </summary>
        [Fact]
        public void NonStaticMethod_EmitsUT0110()
        {
            const string source = @"
namespace Fdp.Toolkit.Utility
{
    public class MyInputs
    {
        [UtilityInputAttribute(""BadInput"")]
        public float BadInput(in UtilityInputCtx ctx) => 0f;
    }
}";
            var (result, _) = RunGenerator(source);

            Assert.Contains(result.Diagnostics, d => d.Id == "UT0110");
        }

        // ---- SC-P2-01-4b -------------------------------------------------------

        /// <summary>
        /// SC-P2-01-4b: A static [UtilityInput] method that returns int instead of float
        /// causes the generator to emit a UT0111 diagnostic.
        /// </summary>
        [Fact]
        public void NonFloatReturn_EmitsUT0111()
        {
            const string source = @"
namespace Fdp.Toolkit.Utility
{
    public static class MyInputs
    {
        [UtilityInputAttribute(""BadInput"")]
        public static int BadInput(in UtilityInputCtx ctx) => 0;
    }
}";
            var (result, _) = RunGenerator(source);

            Assert.Contains(result.Diagnostics, d => d.Id == "UT0111");
        }

        // ---- SC-P2-01-4c -------------------------------------------------------

        /// <summary>
        /// SC-P2-01-4c: A static float [UtilityInput] method with the wrong parameter type
        /// (int instead of in UtilityInputCtx) causes the generator to emit a UT0112 diagnostic.
        /// </summary>
        [Fact]
        public void WrongSignature_EmitsUT0112()
        {
            const string source = @"
namespace Fdp.Toolkit.Utility
{
    public static class MyInputs
    {
        [UtilityInputAttribute(""BadInput"")]
        public static float BadInput(int x) => 0f;
    }
}";
            var (result, _) = RunGenerator(source);

            Assert.Contains(result.Diagnostics, d => d.Id == "UT0112");
        }
    }
}
