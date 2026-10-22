using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fdp.Core;
using Xunit;

namespace Fdp.Tests.Determinism
{
    /// <summary>
    /// ⭐⭐ <c>CE-3302</c> — <b>no id is derived from <c>string.GetHashCode()</c></b>, which .NET randomises per PROCESS. The bus's
    /// managed event type ids were <c>typeof(T).FullName.GetHashCode() &amp; 0x7FFFFFFF</c> ("stable across sessions" — it was not):
    /// a recording's event blocks carry those ids, so a recording written by one process named different types in another, and
    /// a fresh process did not repeat the last one. They now come from <see cref="StableHash"/> (FNV-1a), the same in every process.
    /// <para>⚠ A tripwire, not a proof: it catches the two shapes that produced ids here — <c>.GetHashCode() &amp; 0x7FFFFFFF</c> and a
    /// type's or a name's <c>.Name</c>/<c>.FullName.GetHashCode()</c>. A hash a type computes over its own fields
    /// (<c>override int GetHashCode</c>) is not an id and is not flagged.</para>
    /// </summary>
    public sealed class NoIdFromAProcessRandomisedHashTests
    {
        private static readonly Regex RandomisedId = new(
            @"GetHashCode\(\)\s*&\s*0x7FFFFFFF|\.(FullName|Name)[!?]?\.GetHashCode\(\)", RegexOptions.Compiled);

        [Fact]
        public void StableHash_IsFnv1a32_AndTheSameInEveryProcess()
        {
            Assert.Equal(2166136261u, StableHash.Fnv1a32(""));          // the FNV offset basis
            Assert.Equal(0xE40C292Cu, StableHash.Fnv1a32("a"));         // the published FNV-1a-32 test vector
            Assert.Equal(0xBF9CF968u, StableHash.Fnv1a32("foobar"));
            Assert.True(StableHash.NonNegativeId("foobar") >= 0);
        }

        [Fact]
        public void AManagedEventTypeId_IsTheStableHashOfItsFullName()
        {
            Assert.Equal(StableHash.NonNegativeId(typeof(ManagedProbeEvent).FullName!), FdpEventBus.ManagedTypeId<ManagedProbeEvent>.Id);
            Assert.True(FdpEventBus.TryGetTypeId(typeof(ManagedProbeEvent), out int id, out bool native));
            Assert.False(native);
            Assert.Equal(FdpEventBus.ManagedTypeId<ManagedProbeEvent>.Id, id);
        }

        // ⭐ CE-3303 — System.HashCode is seeded per process too. Legal inside a GetHashCode override (a dictionary key never
        //   leaves the process); anywhere else it is a value that may reach a component, a recording or a golden.
        private static readonly Regex SeededHashCode = new(@"\bHashCode\.Combine\b|\bnew\s+(System\.)?HashCode\s*\(", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> SeededHashCodeExempt = new()
        {
            ["Hrot/Editor/Hrot.Utility.Editor/Emit/UtilityAssetHasher.cs"] =
                "compares an asset's two hashes inside one editor process to pick the hot-reload tier; never stored",
        };

        [Fact]
        public void NoProductionCode_UsesSystemHashCode_OutsideAGetHashCodeOverride()
        {
            var root = RepoRoot();
            Assert.NotNull(root);
            var offenders = new List<string>();
            foreach (var (rel, src) in ProductionSources(root!))
            {
                if (SeededHashCodeExempt.ContainsKey(rel)) continue;
                var lines = src.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var t = lines[i].TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("*")) continue;
                    var code = lines[i];
                    int comment = code.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0) code = code.Substring(0, comment);
                    if (!SeededHashCode.IsMatch(code)) continue;
                    bool inOverride = false;   // the override's signature on this line or within the three above (expression or block body)
                    for (int k = i; k >= Math.Max(0, i - 3); k--)
                        if (lines[k].Contains("GetHashCode(")) { inOverride = true; break; }
                    if (!inOverride)
                        offenders.Add($"{rel}:{i + 1} — System.HashCode is seeded per process; use Fdp.Core.StableHash (or exempt it with its reason)");
                }
            }
            foreach (var stale in SeededHashCodeExempt.Keys.Where(k => !File.Exists(Path.Combine(root!, k))))
                offenders.Add($"{stale}: exempt but gone — remove the exemption");
            Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void StableHash_Combine_IsFnv1aOverTheBytes()
        {
            // FNV-1a over the bytes 01 00 00 00 · 02 00 00 00 · 03 00 00 00 — written out, so a change of the fold shows here.
            uint h = 2166136261u;
            foreach (byte b in new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0 }) { h ^= b; h *= 16777619u; }
            Assert.Equal(unchecked((int)h), StableHash.Combine(1, 2, 3));
            Assert.NotEqual(StableHash.Combine(1, 2, 3), StableHash.Combine(3, 2, 1));
        }

        [Fact]
        public void NoProductionCode_DerivesAnIdFromStringGetHashCode()
        {
            var root = RepoRoot();
            Assert.NotNull(root);
            var offenders = new List<string>();
            foreach (var (rel, src) in ProductionSources(root!))
            {
                var lines = src.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var t = lines[i].TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("*")) continue;
                    var code = lines[i];
                    int comment = code.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0) code = code.Substring(0, comment);
                    if (RandomisedId.IsMatch(code))
                        offenders.Add($"{rel}:{i + 1} — string.GetHashCode() differs per process; use Fdp.Core.StableHash");
                }
            }
            Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
        }

        public sealed class ManagedProbeEvent { public int Value; }

        private static IEnumerable<(string Rel, string Source)> ProductionSources(string root)
        {
            foreach (var top in new[] { "FDP", "Hrot", "Stride" })
            {
                var dir = Path.Combine(root, top);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains("/ExtDeps/")) continue;
                    if (rel.Split('/').Any(p => p.EndsWith(".Tests") || p.EndsWith("Tests") || p == "TestShared" || p.EndsWith(".TestKit")
                                             || p.Contains("Benchmark"))) continue;
                    yield return (rel, File.ReadAllText(file));
                }
            }
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
