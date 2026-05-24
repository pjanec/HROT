using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Behavior.Analyzers;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Roslyn compilation tests for BTreeActionGenerator deactivator detection and emission.
    /// Covers TASK-EQL-004 success conditions T1-T5.
    /// </summary>
    public class BTreeActionGeneratorTests
    {
        // Attribute and type stubs matching production FQNs (by short name).
        private const string CommonStubs = @"
namespace Fbt
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class BTreeActionAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class BTreeConditionAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BTreeDeactivatorAttribute : System.Attribute
    {
        public string TargetAction { get; }
        public BTreeDeactivatorAttribute(string targetAction) { TargetAction = targetAction; }
    }

    public interface IAIContext { }
    public struct BehaviorTreeState { }
    public enum NodeStatus { Running, Success, Failure }
}";

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
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new BTreeActionGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);
            driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
                compilation, out var outputCompilation, out _);

            return (driver.GetRunResult(), outputCompilation);
        }

        // ---- T1: 4-param action + 4-param deactivator ----

        /// <summary>
        /// T1: A 4-param [BTreeAction] method with a companion [BTreeDeactivator] causes
        /// registry.RegisterDeactivator(...) to be emitted in the generated file.
        /// </summary>
        [Fact]
        public void T1_DirectAction_WithDeactivator_EmitsRegisterDeactivatorCall()
        {
            const string source = @"
namespace Foo.Bar
{
    public struct MyBlackboard { }
    public struct MyContext : Fbt.IAIContext { }

    public static class MyNodes
    {
        [Fbt.BTreeAction]
        public static Fbt.NodeStatus Action_X(
            ref MyBlackboard bb, ref Fbt.BehaviorTreeState st, ref MyContext ctx, int p)
            => default;

        [Fbt.BTreeDeactivator(""Foo.Bar.MyNodes.Action_X"")]
        public static void Deactivate_X(
            ref MyBlackboard bb, ref Fbt.BehaviorTreeState st, ref MyContext ctx, int p) { }
    }
}";
            var (result, _) = RunGenerator(source);

            Assert.Equal(1, result.GeneratedTrees.Length);
            string text = result.GeneratedTrees[0].GetText().ToString();
            Assert.Contains(
                "registry.RegisterDeactivator(\"Foo.Bar.MyNodes.Action_X\", global::Foo.Bar.MyNodes.Deactivate_X);",
                text);
        }

        // ---- T2: 3-param bridge action + deactivator with @0 compound key ----

        /// <summary>
        /// T2: A 3-param [BTreeAction] bridge method with a companion [BTreeDeactivator("...@0")]
        /// causes the compound key to be emitted in registry.RegisterDeactivator.
        /// </summary>
        [Fact]
        public void T2_BridgeAction_WithDeactivatorAtZeroSuffix_EmitsRegisterDeactivatorCall()
        {
            const string source = @"
namespace Foo.Bar
{
    public struct MyValue { }
    public struct MyBlackboard { }
    public struct MyContext : Fbt.IAIContext { }

    public static class MyNodes
    {
        [Fbt.BTreeAction]
        public static Fbt.NodeStatus Action_X(
            ref MyBlackboard bb, ref Fbt.BehaviorTreeState st, ref MyContext ctx, int p)
            => default;

        [Fbt.BTreeAction]
        public static Fbt.NodeStatus Action_Y(
            ref MyValue val, ref Fbt.BehaviorTreeState st, ref MyContext ctx)
            => default;

        [Fbt.BTreeDeactivator(""Foo.Bar.MyNodes.Action_Y@0"")]
        public static void Deactivate_Y(
            ref MyBlackboard bb, ref Fbt.BehaviorTreeState st, ref MyContext ctx, int p) { }
    }
}";
            var (result, _) = RunGenerator(source);

            Assert.Equal(1, result.GeneratedTrees.Length);
            string text = result.GeneratedTrees[0].GetText().ToString();
            Assert.Contains(
                "registry.RegisterDeactivator(\"Foo.Bar.MyNodes.Action_Y@0\", global::Foo.Bar.MyNodes.Deactivate_Y);",
                text);
        }

        // ---- T3: empty TargetAction -> BHU_016 diagnostic, no emission ----

        /// <summary>
        /// T3: A [BTreeDeactivator("")] with an empty TargetAction causes diagnostic BHU_016
        /// and no RegisterDeactivator call is emitted.
        /// </summary>
        [Fact]
        public void T3_DeactivatorWithEmptyTarget_EmitsBHU016_NoEmission()
        {
            const string source = @"
namespace Foo.Bar
{
    public struct MyBlackboard { }
    public struct MyContext : Fbt.IAIContext { }

    public static class MyNodes
    {
        [Fbt.BTreeAction]
        public static Fbt.NodeStatus Action_X(
            ref MyBlackboard bb, ref Fbt.BehaviorTreeState st, ref MyContext ctx, int p)
            => default;

        [Fbt.BTreeDeactivator("""")]
        public static void Deactivate_X(
            ref MyBlackboard bb, ref Fbt.BehaviorTreeState st, ref MyContext ctx, int p) { }
    }
}";
            var (result, _) = RunGenerator(source);

            string text = result.GeneratedTrees.Length > 0
                ? result.GeneratedTrees[0].GetText().ToString()
                : string.Empty;
            Assert.DoesNotContain("RegisterDeactivator", text);

            var diag = result.Diagnostics.FirstOrDefault(d => d.Id == "BHU_016");
            Assert.NotNull(diag);
            Assert.Equal(DiagnosticSeverity.Warning, diag!.Severity);
        }

        // ---- T4: unknown TargetAction -> BHU_017 diagnostic, no emission ----

        /// <summary>
        /// T4: A [BTreeDeactivator("Foo.Unknown")] where the target matches no [BTreeAction]
        /// in the compilation causes diagnostic BHU_017 and no emission.
        /// </summary>
        [Fact]
        public void T4_DeactivatorWithUnknownTarget_EmitsBHU017_NoEmission()
        {
            const string source = @"
namespace Foo.Bar
{
    public struct MyBlackboard { }
    public struct MyContext : Fbt.IAIContext { }

    public static class MyNodes
    {
        [Fbt.BTreeAction]
        public static Fbt.NodeStatus Action_X(
            ref MyBlackboard bb, ref Fbt.BehaviorTreeState st, ref MyContext ctx, int p)
            => default;

        [Fbt.BTreeDeactivator(""Foo.Unknown"")]
        public static void Deactivate_X(
            ref MyBlackboard bb, ref Fbt.BehaviorTreeState st, ref MyContext ctx, int p) { }
    }
}";
            var (result, _) = RunGenerator(source);

            string text = result.GeneratedTrees.Length > 0
                ? result.GeneratedTrees[0].GetText().ToString()
                : string.Empty;
            Assert.DoesNotContain("RegisterDeactivator", text);

            var diag = result.Diagnostics.FirstOrDefault(d => d.Id == "BHU_017");
            Assert.NotNull(diag);
            Assert.Equal(DiagnosticSeverity.Warning, diag!.Severity);
        }

        // ---- T5: regression — no deactivators means no RegisterDeactivator lines ----

        /// <summary>
        /// T5: A compilation with only [BTreeAction] methods and no [BTreeDeactivator] methods
        /// produces a generated file with no RegisterDeactivator calls (regression check).
        /// </summary>
        [Fact]
        public void T5_NoDeactivators_GeneratesOutputWithNoRegisterDeactivatorLines()
        {
            const string source = @"
namespace Foo.Bar
{
    public struct MyBlackboard { }
    public struct MyContext : Fbt.IAIContext { }

    public static class MyNodes
    {
        [Fbt.BTreeAction]
        public static Fbt.NodeStatus Action_X(
            ref MyBlackboard bb, ref Fbt.BehaviorTreeState st, ref MyContext ctx, int p)
            => default;
    }
}";
            var (result, _) = RunGenerator(source);

            Assert.Equal(1, result.GeneratedTrees.Length);
            string text = result.GeneratedTrees[0].GetText().ToString();
            Assert.DoesNotContain("RegisterDeactivator", text);
            Assert.Contains("registry.Register(\"Foo.Bar.MyNodes.Action_X\"", text);
        }
    }
}
