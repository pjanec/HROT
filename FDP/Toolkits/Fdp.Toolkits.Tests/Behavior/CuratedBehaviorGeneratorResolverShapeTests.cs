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
    /// block resolver <c>(in TAuthored, ref TBlock, EntityRepository, Entity)</c> (<c>host</c> retired by <c>CE-445</c>), and
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
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class BehaviorResolverAttribute : System.Attribute
    {
        public BehaviorResolverAttribute(string behaviorName) { }
        public System.Type ParamsType { get; set; }
    }
}
namespace Hrot.Map.Definitions.Behavior
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class, AllowMultiple = true)]
    public sealed class BehaviorAttribute : System.Attribute
    {
        public BehaviorAttribute(string name) { }
        public System.Type Params { get; set; }
    }
}
namespace Fbt
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class BTreeDefinitionAttribute : System.Attribute
    {
        public BTreeDefinitionAttribute(string treeName) { }
        public bool Curated { get; set; }
        public System.Type ParamsType { get; set; }
    }
}
namespace Demo
{
    public sealed class GeoIntent { public double Lat; public double Lon; }
    public struct OtherIntent { public int N; }
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
            Fdp.Core.EntityRepository world, Fdp.Core.Entity self)
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
            Fdp.Core.EntityRepository world, Fdp.Core.Entity self)
        { }
    }
}");
            Assert.Contains(diags, d => d.Id == "BEH001");
            Assert.DoesNotContain("RegisterResolver(\"Bad\"", gen);
        }

        /// <summary>⭐ The full delegate shape (5 params since <c>CE-445</c> retired <c>host</c>) is emitted as a
        /// method group, no adapter.</summary>
        [Fact]
        public void TheFullDelegateShape_IsAMethodGroup()
        {
            var (diags, gen) = Run(@"
namespace Demo
{
    public static unsafe class Resolvers
    {
        [Fdp.Toolkit.Behavior.BehaviorResolver(""Six"")]
        public static void Six(string json, byte* memory, int capacity,
            Fdp.Core.EntityRepository world, Fdp.Core.Entity self)
        { }
    }
}");
            Assert.DoesNotContain(diags, d => d.Id == "BEH001");
            Assert.Contains("beh.RegisterResolver(\"Six\", global::Demo.Resolvers.Six);", gen);
            Assert.DoesNotContain("FromBlockResolver", gen);
        }
            // ── ⭐⭐ CE-2134 (docs/DESIGN_Orders_Params_Entity_Classes.md §5b) — a declared params DTO must be what the behaviour parses ──

        /// <summary>⛔ BEH002 — <c>[Behavior(Params = X)]</c> on a behaviour whose TYPED resolver takes a different <c>TAuthored</c>:
        /// the published contract would not be what it parses (CE-2128). ✅ The same declaration naming the resolver's type is clean.</summary>
        [Fact]
        public void CE2134_ADeclaredDtoThatIsNotTheResolversSource_IsBEH002()
        {
            const string Resolver = @"
namespace Demo
{
    public static class Resolvers
    {
        [Hrot.Map.Definitions.Behavior.Behavior(""GeoMove"", Params = typeof(DTO))]
        [Fdp.Toolkit.Behavior.BehaviorResolver(""GeoMove"")]
        public static void ResolveGeoMove(in GeoIntent authored, ref DemoBlock block, Fdp.Core.EntityRepository world, Fdp.Core.Entity self) { }
    }
}";
            Assert.Contains(Run(Resolver.Replace("DTO", "OtherIntent")).Diagnostics, d => d.Id == "BEH002");
            Assert.DoesNotContain(Run(Resolver.Replace("DTO", "GeoIntent")).Diagnostics, d => d.Id == "BEH002");
        }

        /// <summary>⛔ BEH003 — a curated topology with NO resolver has its JSON parsed straight into its <c>ParamsType</c>, so that is
        /// the only honest declaration (and, stored, P3's unmanaged struct). ✅ Declaring the <c>ParamsType</c> itself is clean.</summary>
        [Fact]
        public void CE2134_ADeclaredDtoThatIsNotTheIdentityParsedType_IsBEH003()
        {
            const string Topology = @"
namespace Demo
{
    public static class Trees
    {
        [Hrot.Map.Definitions.Behavior.Behavior(""Patrol"", Params = typeof(DTO))]
        [Fbt.BTreeDefinition(""Patrol"", Curated = true, ParamsType = typeof(DemoBlock))]
        public static object BuildPatrol() => null;
    }
}";
            Assert.Contains(Run(Topology.Replace("DTO", "GeoIntent")).Diagnostics, d => d.Id == "BEH003");
            Assert.DoesNotContain(Run(Topology.Replace("DTO", "DemoBlock")).Diagnostics, d => d.Id == "BEH003");
        }
    }
}
