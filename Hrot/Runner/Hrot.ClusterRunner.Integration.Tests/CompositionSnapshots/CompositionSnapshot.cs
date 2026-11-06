using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;

namespace Hrot.ClusterRunner.Integration.Tests.CompositionSnapshots;

/// <summary>
/// ⭐⭐⭐ S0 (HANDOFF_Coordinator_To_Backend_Multiplayer_Server_And_Full_Cycle, item ①) — WHAT a booted host composed, as a deterministic JSON document.
/// Every section is a list of one-line rows, so a golden diff names the host, the section and the row. ⛔ Nothing here changes from run to run:
/// no node ids, ports, timestamps, counters, profile data or GUIDs (a system is named by its TYPE, never by an instance).
/// Reads only what the hosts already expose; the two private fields it needs (a subsystem's bootstrapper, the world's singleton slots) go through
/// reflection so that NO production code changed for the rail.
/// </summary>
internal static class CompositionSnapshot
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ── reflection (private fields only; the answer is a type, never a value that moves) ──────────────────────────────────

    internal static T? FindField<T>(params object?[] owners) where T : class
    {
        foreach (var owner in owners)
        {
            if (owner is null) continue;
            for (var t = owner.GetType(); t != null; t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    if (typeof(T).IsAssignableFrom(f.FieldType) && f.GetValue(owner) is T v) return v;
        }
        return null;
    }

    internal static object? Field(object? owner, string name)
    {
        for (var t = owner?.GetType(); t != null; t = t.BaseType)
            if (t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly) is { } f)
                return f.GetValue(owner);
        return null;
    }

    // ── the snapshot ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <param name="host">The host's name (the golden's file stem).</param>
    /// <param name="node">The booted node.</param>
    /// <param name="bootstrapper">The node's bootstrapper (capabilities, providers); null when it cannot be reached.</param>
    internal static string Describe(string host, HrotNodeContext node, SharedApplicationBootstrapper? bootstrapper)
    {
        var kernel = node.Kernel;
        var diag = new ArchitectureDiagnosticsService(kernel).GetSnapshot();

        var modules = diag.Modules
            .Select(m => $"{m.ModuleTypeName} | name={m.ModuleName} | run={m.RunMode} | data={m.DataStrategy} | hz={m.TargetFrequencyHz}")
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        var translators = diag.Translators
            .Select(t => $"{t.SystemName} | {t.Direction} | {t.TopicName} | ordinal={t.DescriptorOrdinal}").ToList();   // scheduler order kept

        var systems = SystemRows(kernel);

        var capabilities = (bootstrapper?.Capabilities ?? Array.Empty<INodeCapability>())
            .Select(c => $"{c.Key} | needs=[{string.Join(",", c.Needs.OrderBy(n => n, StringComparer.Ordinal))}]").ToList();   // resolution order kept

        var providers = new List<string>();
        if (Field(bootstrapper, "_resourceProviders") is IEnumerable ps)
            foreach (var p in ps) providers.Add(((INodeResourceProvider)p).Key + " | " + p.GetType().FullName);

        var world = node.World;
        var componentTypes = world.GetRegisteredComponentTypes().Keys.Select(t => t.FullName ?? t.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var singletons = new SortedSet<string>(StringComparer.Ordinal);
        if (Field(world, "_singletons") is object?[] slots)
            foreach (var slot in slots)
                if (slot != null && slot.GetType().IsGenericType && slot.GetType().GenericTypeArguments.Length == 1)
                    singletons.Add(slot.GetType().GenericTypeArguments[0].FullName ?? slot.GetType().GenericTypeArguments[0].Name);

        var root = new JsonObject
        {
            ["host"] = host,
            ["time"] = new JsonObject
            {
                ["role"] = node.TimeRole.ToString(),
                ["controller"] = node.TimeController?.GetType().FullName ?? "none",
                ["networked"] = node.Participant != null,
                ["clusterSlaveInstances"] = CountClusterSlaves(node),
            },
            ["counts"] = new JsonObject
            {
                ["modules"] = modules.Count, ["systems"] = systems.Count, ["translators"] = translators.Count,
                ["capabilities"] = capabilities.Count, ["providers"] = providers.Count,
                ["singletons"] = singletons.Count, ["componentTypes"] = componentTypes.Count,
            },
            ["modules"] = Arr(modules),
            ["systems"] = Arr(systems),
            ["translators"] = Arr(translators),
            ["capabilities"] = Arr(capabilities),
            ["resourceProviders"] = Arr(providers),
            ["singletons"] = Arr(singletons),
            ["componentTypes"] = Arr(componentTypes),
        };
        return root.ToJsonString(Json).Replace("\r\n", "\n") + "\n";
    }

    private static JsonArray Arr(IEnumerable<string> rows) { var a = new JsonArray(); foreach (var r in rows) a.Add(r); return a; }

    /// <summary>Every system in execution order within its phase, group members nested (<c>.NNN</c> per level) under their group.</summary>
    private static List<string> SystemRows(ModuleHostKernel kernel)
    {
        var phaseOf = new Dictionary<IEcsModuleSystem, SystemPhase>(ReferenceEqualityComparer.Instance);
        foreach (var (phase, list) in kernel.SystemScheduler.GetAllProfileData())
            foreach (var (system, _) in list) phaseOf[system] = phase;

        var rows = new List<string>();
        var index = new Dictionary<SystemPhase, int>();
        foreach (var system in kernel.SystemScheduler.GetAllSystems())
        {
            if (!phaseOf.TryGetValue(system, out var phase)) continue;
            int i = index.TryGetValue(phase, out var n) ? n : 0;
            index[phase] = i + 1;
            AddRow(rows, kernel, system, phase, $"{i:D3}", 0);
        }
        return rows.OrderBy(r => PhaseRank(r), Comparer<int>.Default).ToList();   // stable: phases in enum order, execution order kept inside
    }

    private static int PhaseRank(string row) => Enum.TryParse<SystemPhase>(row.Split(' ')[0], out var p) ? (int)p : int.MaxValue;

    private static void AddRow(List<string> rows, ModuleHostKernel kernel, IEcsModuleSystem system, SystemPhase phase, string path, int depth)
    {
        rows.Add($"{phase} {path} {system.GetType().FullName} | module={kernel.GetModuleNameForSystem(system)}");
        if (system is ISystemGroup group && depth < 6)
        {
            int j = 0;
            foreach (var member in group.GetSystems()) AddRow(rows, kernel, member, phase, $"{path}.{j++:D3}", depth + 1);
        }
    }

    /// <summary>Distinct <see cref="ClusterSlave"/> objects the node holds: its context's, plus any a system or base module carries in a field.</summary>
    private static int CountClusterSlaves(HrotNodeContext node)
    {
        var seen = new HashSet<ClusterSlave>(ReferenceEqualityComparer.Instance);
        seen.Add(node.ClusterSlave);
        void Scan(object? o)
        {
            if (o is null) return;
            for (var t = o.GetType(); t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    if (f.FieldType == typeof(ClusterSlave) && f.GetValue(o) is ClusterSlave s) seen.Add(s);
        }
        foreach (var s in node.Kernel.SystemScheduler.GetAllSystems()) { Scan(s); if (s is ISystemGroup g) foreach (var m in g.GetSystems()) Scan(m); }
        foreach (var m in node.BaseModules) Scan(m);
        Scan(node.SlaveTranslator);
        return seen.Count;
    }

    // ── comparing ────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static string NoIndex(string row) => System.Text.RegularExpressions.Regex.Replace(row, @"^(\w+) \d+(\.\d+)* ", "$1 ");

    /// <summary>A readable diff of two goldens, section by section: the first <paramref name="max"/> rows removed / added, or the first row where an equal set differs in ORDER. Empty when equal.</summary>
    internal static string Diff(string host, string expectedJson, string actualJson, int max = 12)
    {
        if (expectedJson == actualJson) return "";
        var sb = new StringBuilder();
        var e = JsonNode.Parse(expectedJson)!.AsObject();
        var a = JsonNode.Parse(actualJson)!.AsObject();
        foreach (var key in e.Select(k => k.Key).Union(a.Select(k => k.Key)).Distinct())
        {
            var ev = e[key]; var av = a[key];
            if (ev is JsonArray ea && av is JsonArray aa)
            {
                var el = ea.Select(x => x!.GetValue<string>()).ToList(); var al = aa.Select(x => x!.GetValue<string>()).ToList();
                if (el.SequenceEqual(al)) continue;
                sb.AppendLine($"[{host}] section '{key}': expected {el.Count} rows, got {al.Count}");
                // compared without the position token, so ONE inserted system is one '+' row, not a cascade of shifted indices
                var ek = el.Select(NoIndex).ToList(); var ak = al.Select(NoIndex).ToList();
                var removed = ek.Except(ak).ToList(); var added = ak.Except(ek).ToList();
                foreach (var r in removed.Take(max)) sb.AppendLine($"    - {r}");
                if (removed.Count > max) sb.AppendLine($"    … {removed.Count - max} more removed");
                foreach (var r in added.Take(max)) sb.AppendLine($"    + {r}");
                if (added.Count > max) sb.AppendLine($"    … {added.Count - max} more added");
                if (removed.Count == 0 && added.Count == 0)
                {
                    int i = 0; while (i < el.Count && i < al.Count && el[i] == al[i]) i++;
                    sb.AppendLine($"    same rows, different ORDER — first difference at row {i}: expected '{(i < el.Count ? el[i] : "")}', got '{(i < al.Count ? al[i] : "")}'");
                }
            }
            else if (ev?.ToJsonString() != av?.ToJsonString())
                sb.AppendLine($"[{host}] section '{key}': expected {ev?.ToJsonString()}, got {av?.ToJsonString()}");
        }
        return sb.ToString();
    }
}
