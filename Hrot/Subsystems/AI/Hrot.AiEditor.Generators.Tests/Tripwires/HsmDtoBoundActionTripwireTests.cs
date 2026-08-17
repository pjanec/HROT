using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Tripwires
{
    /// <summary>
    /// A tripwire for <c>E3</c> — per-occurrence HSM storage.
    ///
    /// <para><b>The hazard.</b> <c>HsmActionGenerator</c> turns every <c>[SharedAiAction]</c> /
    /// <c>[SharedAiCondition]</c> it can see into a thunk that reads its DTO at a <b>baked byte
    /// offset</b> from <c>BrainBlackboard.BehaviorParameters[0]</c>, and registers it under the
    /// compound key <c>{MethodFqn}@{byteOffset}</c>. The offset is a compile-time constant, so the
    /// thunk can only ever read <b>one</b> occurrence's bytes. A second occurrence of the same asset
    /// on one entity reads the first one's parameters — silently, with no diagnostic.</para>
    ///
    /// <para><b>Why a tripwire and not a fix.</b> The population is small and inert today (see the
    /// baseline below), so building the delivery mechanism now would leave two mechanisms in place —
    /// which <c>Q35-C</c> forbids. This test makes the hazard announce itself the day someone adds a
    /// subject to it, instead of letting it stay latent.</para>
    ///
    /// <para><b>This is not a ban.</b> Tripping it does not mean "you may not do this". It means the
    /// change now needs <c>E3</c>, whose design is finished and waiting — see
    /// <c>Architect_Question_35_Hsm_Occurrence_Delivery.md</c> (RESOLVED) and
    /// <c>DESIGN_Hsm_Storage_Model.md</c> §3.</para>
    /// </summary>
    public sealed class HsmDtoBoundActionTripwireTests
    {
        /// <summary>
        /// The four entries that exist today, named individually so that adding a fifth is what
        /// reddens this test — not the mere existence of any.
        ///
        /// All four are declared in <c>Fdp.Toolkits</c>, which is generator-bearing, so
        /// <c>HsmActionGenerator</c> does emit an HSM thunk for each (they are visible in
        /// <c>Fdp.Toolkits.dll</c> as <c>Action_{Name}_At0</c>). They are <b>inert</b> for two
        /// separate reasons, and both must hold for the exemption to be honest:
        ///
        /// <list type="number">
        ///   <item>nothing calls <c>Fdp.Toolkits.Generated.HsmActionRegistrar.RegisterAll()</c>, so
        ///         the thunks are never registered with <c>HsmActionDispatcher</c>; and</item>
        ///   <item>every one is at offset <c>0</c>, so even a registered thunk would read the first
        ///         DTO in the blackboard rather than a wrong one.</item>
        /// </list>
        ///
        /// They carry <c>[SharedAiAction]</c> for the <b>BTree</b> host; the HSM thunk is incidental —
        /// both generators key on the same attribute.
        /// </summary>
        private static readonly IReadOnlyList<string> Baseline = new[]
        {
            "Fdp.Toolkits :: BlueprintLifecycleLibrary.AttachInstanceBlueprint",
            "Fdp.Toolkits :: BlueprintLifecycleLibrary.RemoveInstanceBlueprint",
            "Fdp.Toolkits :: BlueprintLifecycleLibrary.ReplaceInstanceBlueprint",
            "Fdp.Toolkits :: DemoSharedActions.AlertNearbyUnits",
        };

        private const string PointerToTheDesign =
            "This is NOT a ban — it means the change now needs E3, per-occurrence HSM storage, whose " +
            "design is finished and waiting:\n" +
            "  * docs/blueprints/Architect_Question_35_Hsm_Occurrence_Delivery.md (RESOLVED: carry the " +
            "(regionSlotIndex, stateId) pair on HsmCommandWriter; ONE delivery path, not two)\n" +
            "  * docs/blueprints/DESIGN_Hsm_Storage_Model.md section 3\n" +
            "Build E3, then move the new entry out of this test's Baseline.";

        [Fact]
        public void NoNewDtoBoundHsmAction_ExistsInAGeneratorBearingAssembly()
        {
            var repoRoot = ResolveRepoRoot();
            var projects = GeneratorBearingProjects(repoRoot);

            // If this ever finds nothing, the DERIVATION broke — not the codebase. A tripwire that
            // scans an empty set passes forever, which is the failure mode this programme keeps
            // finding, so it is asserted rather than assumed.
            Assert.True(
                projects.Count >= 5,
                $"expected the generator-bearing project set to be derived from the csproj files, got " +
                $"{projects.Count} under '{repoRoot}' — the derivation is broken, not the codebase");

            var found = projects
                .SelectMany(p => ScanDirectory(Path.GetDirectoryName(p)!, ProjectName(p)))
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .ToList();

            var added = found.Where(e => !Baseline.Contains(e.Key)).ToList();

            Assert.True(added.Count == 0,
                "A DTO-BOUND HSM ACTION APPEARED IN A GENERATOR-BEARING ASSEMBLY.\n\n" +
                string.Join("\n", added.Select(e => $"  {e.Key}   ({e.File}:{e.Line})")) +
                "\n\nHsmActionGenerator will emit a thunk that reads this DTO at a BAKED byte offset " +
                "from BrainBlackboard.BehaviorParameters[0] and register it as {MethodFqn}@{offset}. " +
                "That offset is a compile-time constant, so a second occurrence of the asset on one " +
                "entity reads the first occurrence's bytes, silently.\n\n" +
                PointerToTheDesign);

            // The other half: a baseline entry that DISAPPEARS is also a finding — it means the
            // population moved and the exemption above may no longer describe reality.
            var missing = Baseline.Where(b => found.All(e => e.Key != b)).ToList();
            Assert.True(missing.Count == 0,
                "A baselined DTO-bound HSM action is gone: " + string.Join(", ", missing) +
                "\nThat is fine, but the Baseline comment above no longer describes the code — " +
                "update it (or, if the population reached zero, delete the entry).");
        }

        /// <summary>
        /// Non-vacuity. The scan above passes today because nothing new exists; this drives the SAME
        /// <see cref="ScanDirectory"/> over a synthetic source file and proves it can see one at all.
        /// Without this the green above would be indistinguishable from a scanner that finds nothing.
        /// </summary>
        [Fact]
        public void TheScan_SeesADtoBoundAction_WhenOneIsPresent()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hsm-tripwire-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "Synthetic.cs"), @"
namespace Synthetic
{
    public static class SyntheticActions
    {
        [Fbt.Kernel.SharedAiAction(typeof(Slot), ""Params"")]
        public static void DoesSomething(ref int p) { }

        [SharedAiCondition(typeof(Slot), ""Params"")]
        public static bool AlsoAGuard(ref int p) => true;

        // Skipped: the generator ignores non-public methods (GetMethodInfo bails on
        // Private/Protected/ProtectedAndInternal), so the tripwire must ignore them too.
        [SharedAiAction(typeof(Slot), ""Params"")]
        private static void NotVisibleToTheGenerator(ref int p) { }

        // Skipped: an unrelated attribute must not trip it.
        [Fhsm.Kernel.Attributes.HsmAction(Name = ""Plain"")]
        public static void PlainHsmAction() { }
    }
}");
                var found = ScanDirectory(dir, "Synthetic").Select(e => e.Key).ToList();

                Assert.Equal(
                    new[]
                    {
                        "Synthetic :: SyntheticActions.AlsoAGuard",
                        "Synthetic :: SyntheticActions.DoesSomething",
                    },
                    found.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // ---- the scan ----------------------------------------------------------

        private readonly record struct Entry(string Key, string File, int Line);

        /// <summary>
        /// The four attributes <c>HsmActionGenerator.GetMethodInfo</c> keys on. Matched by simple
        /// name because this is a syntax-level scan; the generator matches the fully-qualified symbol,
        /// so a type with one of these names in another namespace would over-trigger here. That
        /// direction is the safe one for a tripwire.
        /// </summary>
        private static readonly string[] DtoBindingAttributes =
        {
            "SharedAiAction", "SharedAiCondition", "SharedAiHeavyAction", "SharedAiHeavyCondition",
        };

        private static IEnumerable<Entry> ScanDirectory(string dir, string projectName)
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(file)) continue;

                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
                foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (!method.AttributeLists.SelectMany(l => l.Attributes).Any(IsDtoBinding)) continue;

                    // Mirror the generator's own accessibility filter: GetMethodInfo bails on
                    // Private, Protected and ProtectedAndInternal ("private protected"), and a
                    // method with no accessibility modifier is private by default.
                    if (!IsVisibleToTheGenerator(method.Modifiers)) continue;

                    var owner = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                    var line  = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    yield return new Entry(
                        $"{projectName} :: {owner?.Identifier.Text ?? "?"}.{method.Identifier.Text}",
                        file, line);
                }
            }
        }

        private static bool IsVisibleToTheGenerator(SyntaxTokenList mods)
        {
            bool isPublic    = mods.Any(SyntaxKind.PublicKeyword);
            bool isInternal  = mods.Any(SyntaxKind.InternalKeyword);
            bool isProtected = mods.Any(SyntaxKind.ProtectedKeyword);
            bool isPrivate   = mods.Any(SyntaxKind.PrivateKeyword);

            if (isPublic) return true;
            if (isProtected && isInternal) return true;   // protected internal — ProtectedOrInternal
            if (isInternal && !isPrivate) return true;    // internal
            return false;                                 // private / protected / private protected / none
        }

        private static bool IsDtoBinding(AttributeSyntax attr)
        {
            var name = attr.Name.ToString();
            var simple = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
            if (simple.EndsWith("Attribute", StringComparison.Ordinal))
                simple = simple[..^"Attribute".Length];
            return DtoBindingAttributes.Contains(simple, StringComparer.Ordinal);
        }

        private static bool IsBuildOutput(string path)
        {
            var normalised = path.Replace('\\', '/');
            return normalised.Contains("/obj/", StringComparison.Ordinal)
                || normalised.Contains("/bin/", StringComparison.Ordinal);
        }

        // ---- deriving the generator-bearing set --------------------------------

        /// <summary>
        /// A project is generator-bearing when it references <c>Fdp.Toolkits.Analyzers</c> with
        /// <c>OutputItemType="Analyzer"</c> — that, and only that, is what makes
        /// <c>HsmActionGenerator</c> run over its source. Derived rather than listed, so a new
        /// generator-bearing project is covered the day it is created.
        /// </summary>
        private static IReadOnlyList<string> GeneratorBearingProjects(string repoRoot)
        {
            var refPattern = new Regex(
                @"<ProjectReference\b(?<body>(?:(?!</?ProjectReference)[\s\S])*?)(?:/>|</ProjectReference>)",
                RegexOptions.Compiled);

            var result = new List<string>();
            foreach (var csproj in Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(csproj)) continue;
                var text = File.ReadAllText(csproj);
                foreach (Match m in refPattern.Matches(text))
                {
                    var body = m.Value;
                    if (body.Contains("Fdp.Toolkits.Analyzers.csproj", StringComparison.Ordinal) &&
                        body.Contains("OutputItemType=\"Analyzer\"", StringComparison.Ordinal))
                    {
                        result.Add(csproj);
                        break;
                    }
                }
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static string ProjectName(string csprojPath)
            => Path.GetFileNameWithoutExtension(csprojPath);

        private static string ResolveRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "IOS-IG-SimHost.sln"))) return dir;
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            throw new InvalidOperationException(
                "Could not find repo root (looked for IOS-IG-SimHost.sln upward from " +
                AppContext.BaseDirectory + ").");
        }
    }
}
