using System;
using System.Collections.Immutable;
using System.Linq;
using Fdp.Toolkit.Behavior.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-427</c> — the curated generator accepts a THIRD <c>[BehaviorResolver]</c> shape: the typed
    /// block resolver <c>(in TAuthored, ref TBlock, EntityRepository, Entity, IHostVariableAccess?)</c>, and
    /// adapts it through <c>BehaviorParams.FromBlockResolver</c>. 📄 <c>Q76</c> §12.19.
    ///
    /// <para>⚠ Driven on an in-memory source, not the shipped assembly: no shipped resolver uses the typed
    /// shape yet, and migrating one (<c>MoveToLocation</c>) would change its JSON options — a behaviour
    /// change nobody asked for. The runtime half — what the adapter DOES — is pinned by
    /// <c>BehaviorParamsBlockResolverTests</c>.</para>
    /// </summary>
    public sealed class CuratedBehaviorGeneratorResolverShapeTests
    {
        private const string Stubs = @"
namespace Fdp.Core { public class EntityRepository { } public struct Entity { } }
namespace Fdp.Toolkit.Behavior
{
    public interface IHostVariableAccess { }
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class BehaviorResolverAttribute : System.Attribute
    {
        public BehaviorResolverAttribute(string behaviorName) { }
        public System.Type ParamsType { get; set; }
    }
}
namespace Demo
{
    public sealed class GeoIntent { public double Lat; public double Lon; }
    public struct DemoBlock { public float X; public float Y; public int Ticks; }
}
";

        private static (ImmutableArray<Diagnostic> Diagnostics, string Generated) Run(string userSource)
        {
            var tree = CSharpSyntaxTree.ParseText(userSource + "\n" + Stubs);
            var platform = ((string)(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty))
                .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var refs = platform
                .Where(p => System.IO.Path.GetFileName(p) is "System.Private.CoreLib.dll" or "System.Runtime.dll")
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToList();
            var compilation = CSharpCompilation.Create("CuratedShapeTest", new[] { tree }, refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                    nullableContextOptions: NullableContextOptions.Disable));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new CuratedBehaviorGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

            var original = new System.Collections.Generic.HashSet<SyntaxTree>(compilation.SyntaxTrees);
            string generated = string.Concat(output.SyntaxTrees.Where(t => !original.Contains(t))
                                                              .Select(t => t.GetText().ToString()));
            return (driver.GetRunResult().Diagnostics, generated);
        }

        /// <summary>
        /// ⭐⭐ A typed block resolver is registered through <c>FromBlockResolver&lt;TAuthored, TBlock&gt;</c>,
        /// by NAME (<c>R-132</c>'s curated binding surface), with both types fully qualified.
        ///
        /// <para>⚠ Inverse-edit red-proof: drop the <c>isBlock</c> arm in <c>CuratedBehaviorGenerator</c>'s
        /// shape check and this method is reported as <c>BEH001</c> and never registered.</para>
        /// </summary>
        [Fact]
        public void ATypedBlockResolver_IsRegisteredThroughFromBlockResolver()
        {
            var (diags, gen) = Run(@"
namespace Demo
{
    public static class Resolvers
    {
        [Fdp.Toolkit.Behavior.BehaviorResolver(""GeoMove"")]
        public static void ResolveGeoMove(in GeoIntent authored, ref DemoBlock block,
            Fdp.Core.EntityRepository world, Fdp.Core.Entity self, Fdp.Toolkit.Behavior.IHostVariableAccess host)
        { }
    }
}");
            Assert.DoesNotContain(diags, d => d.Id == "BEH001");
            Assert.Contains("beh.RegisterResolver(\"GeoMove\",", gen);
            Assert.Contains("global::Fdp.Toolkit.Behavior.BehaviorParams.FromBlockResolver<global::Demo.GeoIntent, global::Demo.DemoBlock>(", gen);
            Assert.Contains("global::Demo.Resolvers.ResolveGeoMove)", gen);
        }

        /// <summary>
        /// ⛔ The shape is recognised by the REF-KINDS of its first two parameters, never by arity alone:
        /// five by-value parameters are still an invalid resolver.
        /// </summary>
        [Fact]
        public void FiveByValueParameters_AreStillRejected()
        {
            var (diags, gen) = Run(@"
namespace Demo
{
    public static class Resolvers
    {
        [Fdp.Toolkit.Behavior.BehaviorResolver(""Bad"")]
        public static void Bad(GeoIntent authored, DemoBlock block,
            Fdp.Core.EntityRepository world, Fdp.Core.Entity self, Fdp.Toolkit.Behavior.IHostVariableAccess host)
        { }
    }
}");
            Assert.Contains(diags, d => d.Id == "BEH001");
            Assert.DoesNotContain("RegisterResolver(\"Bad\"", gen);
        }

        /// <summary>⭐ The existing 6-param shape is emitted exactly as before — a method group, no adapter.</summary>
        [Fact]
        public void TheSixParamShape_IsUnchanged()
        {
            var (diags, gen) = Run(@"
namespace Demo
{
    public static unsafe class Resolvers
    {
        [Fdp.Toolkit.Behavior.BehaviorResolver(""Six"")]
        public static void Six(string json, byte* memory, int capacity,
            Fdp.Core.EntityRepository world, Fdp.Core.Entity self, Fdp.Toolkit.Behavior.IHostVariableAccess host)
        { }
    }
}");
            Assert.DoesNotContain(diags, d => d.Id == "BEH001");
            Assert.Contains("beh.RegisterResolver(\"Six\", global::Demo.Resolvers.Six);", gen);
            Assert.DoesNotContain("FromBlockResolver", gen);
        }
    }
}
