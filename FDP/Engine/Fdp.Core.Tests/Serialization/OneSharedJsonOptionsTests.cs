using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fdp.Core.Serialization;
using Xunit;

namespace Fdp.Tests.Serialization
{
    /// <summary>
    /// ⭐⭐ <c>CE-3187</c> (R-2005 / R-2007, docs/DESIGN_Orders_Params_Entity_Classes.md §13.1 F12) — every production JSON path uses
    /// the ONE shared options instance, <see cref="FdpJsonOptionsRegistry.DefaultRelaxed"/> / <c>Indented</c>, or a copy of it with a
    /// named delta made by <see cref="FdpJsonOptionsRegistry.Derive"/>. A <c>new JsonSerializerOptions</c> anywhere else is either on
    /// <see cref="Exempt"/> with its one-line reason, or a finding.
    /// <para>⭐ The allow-list may only SHRINK: a listed file whose count drops below its entry fails too, so the entry is lowered
    /// (or removed) in the same change that removed the construction.</para>
    /// <para>⚠ A source scan, so a tripwire, not a proof: it sees the two spellings used in this repo (<c>new JsonSerializerOptions</c>
    /// and a target-typed <c>JsonSerializerOptions x = new(</c>). Test assemblies are out of scope on purpose.</para>
    /// </summary>
    public sealed class OneSharedJsonOptionsTests
    {
        /// <summary>The files allowed to construct their own options, with how many and why. Shrink only.</summary>
        private static readonly Dictionary<string, (int Count, string Why)> Exempt = new()
        {
            ["FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs"] = (3, "the registry itself: DefaultRelaxed, Indented and Derive"),
            // External libraries under FDP/ExtDeps carry no Fdp.Core reference by design.
            ["FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeSchemaExporter.cs"] = (1, "FastBTree is an external library with no Fdp.Core reference"),
            ["FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs"] = (1, "FastBTree is an external library with no Fdp.Core reference"),
            ["FDP/ExtDeps/GizmoMap/GizmoMap.Example/Scenarios/DemoSceneGenerator.cs"] = (1, "GizmoMap is an external library with no Fdp.Core reference"),
            ["FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/UI/MainMenuAdapter.cs"] = (1, "GizmoMap is an external library with no Fdp.Core reference"),
            ["FDP/ExtDeps/StructEdit/src/StructEdit.Core/Memory/BoxedStructEditBuffer.cs"] = (1, "StructEdit is an external library with no Fdp.Core reference"),
            ["FDP/ExtDeps/StructEdit/src/StructEdit.Core/Memory/ManagedObjectEditBuffer.cs"] = (1, "StructEdit is an external library with no Fdp.Core reference"),
            // netstandard2.0 assemblies (read by the source generators) cannot reference net8.0 Fdp.Core. Their enums are strict
            // (JsonStringEnumConverter with integers refused, R-2007).
            ["Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/BTree/BTreeJsonServices.cs"] = (1, "netstandard2.0, cannot reference Fdp.Core; the BTree asset format"),
            ["Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Hsm/HsmJsonServices.cs"] = (1, "netstandard2.0, cannot reference Fdp.Core; the HSM asset format"),
            ["Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Params/ParamsDtoAsset.cs"] = (1, "netstandard2.0, cannot reference Fdp.Core; settings are a subset of Indented"),
            ["Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/BlueprintJsonServices.cs"] = (2, "shared netstandard2.0/net8.0 source read by the generator; the blueprint asset format"),
            ["Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/DebugMapSerializer.cs"] = (1, "netstandard2.0 build; a camelCase debug-map contract"),
            ["Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Transform/GraphFragmentCloner.cs"] = (1, "netstandard2.0 build; an in-memory deep copy"),
            // Published contracts the shared converters would break.
            ["Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiService.cs"] = (1, "SpawnTransformJsonOptions: the documented spawn body writes vectors as {x,y,z} objects"),
            ["Stride/HrotStrideApp.Game/CameraBookmarkStore.cs"] = (1, "per-user bookmark files store vectors as objects; switching drops them until a legacy reader exists"),
        };

        private static readonly Regex Construction =
            new(@"new\s+(global::)?(System\.Text\.Json\.)?JsonSerializerOptions\b|JsonSerializerOptions\??\s+\w+\s*=\s*new\s*\(", RegexOptions.Compiled);

        [Fact]
        public void EveryProductionOptionsConstruction_IsTheRegistryOrAnExemptionWithAReason()
        {
            var root = RepoRoot();
            Assert.NotNull(root);

            var found = Scan(root!);
            var problems = new List<string>();

            foreach (var (file, count) in found)
            {
                if (!Exempt.TryGetValue(file, out var allowed))
                    problems.Add($"{file}: {count} ad-hoc JsonSerializerOptions — use FdpJsonOptionsRegistry.DefaultRelaxed / Indented / Derive, or add an exemption with its reason");
                else if (count > allowed.Count)
                    problems.Add($"{file}: {count} constructions, the exemption allows {allowed.Count}");
            }

            foreach (var (file, allowed) in Exempt)
            {
                found.TryGetValue(file, out var count);
                if (count < allowed.Count)
                    problems.Add($"{file}: the exemption allows {allowed.Count} but {count} remain — shrink the entry");
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        [Fact]
        public void EveryExemption_SaysWhy()
        {
            Assert.All(Exempt, e => Assert.False(string.IsNullOrWhiteSpace(e.Value.Why), e.Key));
            Assert.All(NoOptionsExempt, e => Assert.False(string.IsNullOrWhiteSpace(e.Value.Why), e.Key));
        }

        /// <summary>
        /// ⭐ <c>CE-3191</c> — the files allowed a serializer call with NO options argument (the platform defaults: no fields,
        /// case-sensitive, numeric enums). Shrink only.
        /// </summary>
        private static readonly Dictionary<string, (int Count, string Why)> NoOptionsExempt = new()
        {
            ["FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Bookmarks/BookmarkStore.cs"] = (2, "NodeEdit is an external library with no Fdp.Core reference; its DTO has only strings and numbers"),
            ["Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Params/ParamsDtoAsset.cs"] = (2, "netstandard2.0; quotes a string and compacts a JsonElement"),
            ["Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs"] = (1, "netstandard2.0 build; serializes a JsonNode (a DOM ignores options)"),
        };

        private static readonly Regex SerializerCall = new(
            @"(?<![A-Za-z])JsonSerializer\.(Serialize|Deserialize|SerializeToNode|SerializeToElement|SerializeToDocument|SerializeToUtf8Bytes)(<[^()]*?>)?\(",
            RegexOptions.Compiled);

        /// <summary>
        /// ⭐⭐ <c>CE-3191</c> (R-2005 / R-2007) — no production <c>JsonSerializer</c> call passes ONE argument, so none falls back to the
        /// defaults. ⚠ A tripwire: a two-argument call naming a <c>Type</c> (<c>Deserialize(json, type)</c>) is not caught, and a
        /// call inside a string literal (generated code) is skipped.
        /// </summary>
        [Fact]
        public void EveryProductionSerializerCall_PassesOptions_OrIsAnExemptionWithAReason()
        {
            var root = RepoRoot();
            Assert.NotNull(root);

            var found = new Dictionary<string, List<int>>();
            foreach (var (rel, src) in ProductionSources(root!))
            {
                foreach (Match m in SerializerCall.Matches(src))
                {
                    int lineStart = src.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                    string before = src.Substring(lineStart, m.Index - lineStart);
                    string t = before.TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("*")) continue;
                    if (before.Count(c => c == '"') % 2 == 1) continue;   // inside a string literal on this line
                    var (commas, empty) = TopLevelArguments(src, m.Index + m.Length);
                    if (commas == 0 && !empty)
                    {
                        if (!found.TryGetValue(rel, out var lines)) found[rel] = lines = new List<int>();
                        lines.Add(src.Take(m.Index).Count(c => c == '\n') + 1);
                    }
                }
            }

            var problems = new List<string>();
            foreach (var (file, lines) in found)
            {
                if (!NoOptionsExempt.TryGetValue(file, out var allowed))
                    problems.Add($"{file}:{string.Join(",", lines)}: a JsonSerializer call with no options — pass FdpJsonOptionsRegistry.DefaultRelaxed (or a Derive)");
                else if (lines.Count > allowed.Count)
                    problems.Add($"{file}: {lines.Count} no-options calls, the exemption allows {allowed.Count}");
            }
            foreach (var (file, allowed) in NoOptionsExempt)
            {
                int count = found.TryGetValue(file, out var l) ? l.Count : 0;
                if (count < allowed.Count)
                    problems.Add($"{file}: the exemption allows {allowed.Count} but {count} remain — shrink the entry");
            }
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        private static (int Commas, bool Empty) TopLevelArguments(string src, int i)
        {
            int depth = 1, commas = 0;
            bool empty = true;
            for (int k = i; k < src.Length && depth > 0; k++)
            {
                char c = src[k];
                if (c == '"')
                {
                    empty = false;
                    for (k++; k < src.Length && src[k] != '"'; k++)
                        if (src[k] == '\\') k++;
                }
                else if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (c == ',' && depth == 1) commas++;
                else if (!char.IsWhiteSpace(c)) empty = false;
            }
            return (commas, empty);
        }

        private static IEnumerable<(string Rel, string Source)> ProductionSources(string root)
        {
            foreach (var top in new[] { "FDP", "Hrot", "Stride" })
            {
                var dir = Path.Combine(root, top);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                    if (rel.Split('/').Any(p => p.EndsWith(".Tests") || p.EndsWith("Tests") || p == "TestShared" || p.EndsWith(".TestKit"))) continue;
                    yield return (rel, File.ReadAllText(file));
                }
            }
        }

        [Fact]
        public void Derive_CopiesTheSharedSettings_AppliesTheDelta_AndFreezes()
        {
            var opts = FdpJsonOptionsRegistry.Derive(o => o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

            Assert.True(opts.IncludeFields);
            Assert.True(opts.PropertyNameCaseInsensitive);
            Assert.Same(JsonNamingPolicy.CamelCase, opts.PropertyNamingPolicy);
            Assert.Contains(opts.Converters, c => c is Fdp.Core.Serialization.Converters.StrictStringEnumConverter);
            Assert.Throws<InvalidOperationException>(() => opts.WriteIndented = true);
            Assert.Equal("{\"mode\":\"Beta\"}", JsonSerializer.Serialize(new Probe { Mode = ProbeMode.Beta }, opts));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Probe>("{\"mode\":1}", opts));
        }

        private enum ProbeMode { Alpha, Beta }
        private sealed class Probe { public ProbeMode Mode { get; set; } }

        private static Dictionary<string, int> Scan(string root)
        {
            var found = new Dictionary<string, int>();
            foreach (var top in new[] { "FDP", "Hrot", "Stride" })
            {
                var dir = Path.Combine(root, top);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                    if (rel.Split('/').Any(p => p.EndsWith(".Tests") || p.EndsWith("Tests") || p == "TestShared" || p.EndsWith(".TestKit"))) continue;
                    int n = 0;
                    foreach (var line in File.ReadLines(file))
                    {
                        var t = line.TrimStart();
                        if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) continue;
                        n += Construction.Matches(line).Count;
                    }
                    if (n > 0) found[rel] = n;
                }
            }
            return found;
        }

        private static string? RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "HROT.sln"))) return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
        }
    }
}
