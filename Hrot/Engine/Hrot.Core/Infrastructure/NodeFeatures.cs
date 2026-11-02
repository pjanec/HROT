using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Common.Infrastructure;

/// <summary>⭐ Node Features F-21 — a feature's configuration; <see cref="Enabled"/> is its minimum. Implemented by an UNMANAGED struct.</summary>
public interface IFeatureConfig
{
    bool Enabled { get; }
}

/// <summary>The shared config of every switch-only feature.</summary>
public struct FeatureSwitch : IFeatureConfig
{
    public bool Enabled;
    bool IFeatureConfig.Enabled => Enabled;
}

/// <summary>F-9 — when a change of the feature takes effect.</summary>
public enum FeatureChange : byte { Live, AtLoad }

/// <summary>Typed operations over one feature's config slot (a one-element array of its struct). Built by <see cref="FeatureDeclaration.Of{T}"/>.</summary>
public abstract class FeatureConfigOps
{
    public abstract Type ConfigType { get; }
    /// <summary>A fresh slot holding the type's own initialisers.</summary>
    public abstract Array NewSlot();
    /// <summary>F-22/F-26 — overlays a flat JSON object; an unknown key or malformed JSON THROWS (the applying node refuses).</summary>
    public abstract void Overlay(Array slot, string? json);
    public abstract bool IsEnabled(Array slot);
    public abstract string ToJson(Array slot);
}

internal sealed class FeatureConfigOps<T> : FeatureConfigOps where T : struct, IFeatureConfig
{
    public override Type ConfigType => typeof(T);
    public override Array NewSlot() => new T[] { ParamsDefaults.New<T>() };

    public override void Overlay(Array slot, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        var opts = BehaviorParams.JsonOptions;
        var incoming = JsonNode.Parse(json!) as JsonObject
            ?? throw new JsonException($"a feature override for '{typeof(T).Name}' must be a JSON object (the config's fields, flat).");
        var known = (JsonNode.Parse(JsonSerializer.Serialize(((T[])slot)[0], opts)) as JsonObject)!;
        foreach (var kv in incoming)
        {
            bool found = false;
            foreach (var k in known)
                if (string.Equals(k.Key, kv.Key, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
            if (!found) throw new JsonException($"'{typeof(T).Name}' has no field '{kv.Key}'.");
        }
        BehaviorParams.OverlayFlat(ref ((T[])slot)[0], json, opts);
    }

    public override bool IsEnabled(Array slot) => ((T[])slot)[0].Enabled;
    public override string ToJson(Array slot) => JsonSerializer.Serialize(((T[])slot)[0], BehaviorParams.JsonOptions);
}

/// <summary>⭐ Node Features F-2 — a named behaviour declared by the ONE capability whose code implements it.</summary>
public sealed record FeatureDeclaration(
    string Id,
    string Description,
    FeatureChange Change,
    FeatureConfigOps Ops,
    Func<NodeRole, string?>? RoleDefault = null)
{
    /// <summary>The config type.</summary>
    public Type ConfigType => Ops.ConfigType;

    /// <summary>F-6 — the composition default for a role: a JSON object (partial), or null for none.</summary>
    public string? DefaultFor(NodeRole role) => RoleDefault?.Invoke(role);

    /// <summary>A declaration with config type <typeparamref name="T"/>.</summary>
    public static FeatureDeclaration Of<T>(string id, string description, FeatureChange change, Func<NodeRole, string?>? roleDefault = null)
        where T : struct, IFeatureConfig
        => new(id, description, change, new FeatureConfigOps<T>(), roleDefault);

    /// <summary>A switch-only feature; <paramref name="onFor"/> says for which roles it is ON by default.</summary>
    public static FeatureDeclaration Switch(string id, string description, FeatureChange change, Func<NodeRole, bool>? onFor = null)
        => Of<FeatureSwitch>(id, description, change, onFor == null ? null : r => onFor(r) ? "{\"Enabled\":true}" : "{\"Enabled\":false}");
}

/// <summary>⭐ The declarations of the composed capabilities, with dense indices. Built once at boot; an id declared twice THROWS.</summary>
public sealed class FeatureRegistry
{
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
    private readonly List<FeatureDeclaration> _all = new();

    public FeatureRegistry(IEnumerable<FeatureDeclaration> declarations)
    {
        foreach (var d in declarations)
        {
            if (_index.ContainsKey(d.Id))
                throw new InvalidOperationException($"Feature '{d.Id}' is declared twice — exactly ONE capability declares a feature (F-2).");
            _index[d.Id] = _all.Count;
            _all.Add(d);
        }
    }

    public IReadOnlyList<FeatureDeclaration> All => _all;
    public bool Available(string id) => _index.ContainsKey(id);
    public bool TryIndex(string id, out int index) => _index.TryGetValue(id, out index);

    /// <summary>The advertised availability token of a feature (tier ②).</summary>
    public static string TokenOf(string id) => "hrot.feature." + id;
}

/// <summary>
/// ⭐ Node Features — the ONE reader, per node. Layers per feature: type initialisers ▸ role default ▸ node override ▸ cluster override
/// (F-22), each applied with <c>BehaviorParams.OverlayFlat</c>, recomputed only when a layer changes (F-25).
/// </summary>
public sealed class NodeFeatures : IFeatureGate, IFeatureRecording
{
    private readonly FeatureRegistry _registry;
    private readonly NodeRole _role;
    private readonly Func<string> _nodeName;
    private readonly Array[] _slots;
    private readonly bool[] _on;
    private readonly Dictionary<string, List<string>> _nodeOverrides = new(StringComparer.Ordinal);
    private IReadOnlyList<FeatureOverrideSpec> _cluster = Array.Empty<FeatureOverrideSpec>();
    private readonly Dictionary<string, string> _frozenLayer = new(StringComparer.Ordinal);   // AtLoad: the cluster layer stamped at load
    private readonly Dictionary<string, string> _frozenFull = new(StringComparer.Ordinal);    // AtLoad: a recorded FULL config (branch to live)
    private readonly List<string> _problems = new();

    public NodeFeatures(FeatureRegistry registry, NodeRole role, Func<string>? nodeName = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _role = role;
        _nodeName = nodeName ?? (() => "");
        _slots = new Array[registry.All.Count];
        _on = new bool[registry.All.Count];
        Recompute();
    }

    public FeatureRegistry Registry => _registry;

    /// <summary>After a recompute.</summary>
    public event Action? Changed;

    /// <summary>Layers a node refused (unknown key, malformed JSON) — reported on the heartbeat (F-26).</summary>
    public IReadOnlyList<string> Problems => _problems;

    public bool On(FeatureHandle feature) => _on[feature.Index];

    public bool On(string id) => _on[Resolve(id).Index];

    public FeatureHandle Resolve(string id)
        => _registry.TryIndex(id, out int i)
            ? new FeatureHandle(i)
            : throw new InvalidOperationException($"Feature '{id}' is not declared by any composed capability (F-18).");

    /// <summary>F-25 — the effective config, no allocation. <typeparamref name="T"/> must be the declared config type.</summary>
    public ref readonly T Config<T>(FeatureHandle feature) where T : struct, IFeatureConfig
    {
        if (_slots[feature.Index] is not T[] slot)
            throw new InvalidOperationException($"Feature '{_registry.All[feature.Index].Id}' has config type {_registry.All[feature.Index].ConfigType.Name}, not {typeof(T).Name}.");
        return ref slot[0];
    }

    /// <summary>The effective full config of a feature as JSON (for the load payload, the recording and <c>GET /features</c>).</summary>
    public string ConfigJson(FeatureHandle feature) => _registry.All[feature.Index].Ops.ToJson(_slots[feature.Index]);

    /// <summary>F-7 — adds a node override layer (a partial JSON object). Later calls for one feature stack in order.</summary>
    public void SetNodeOverride(string id, string json)
    {
        Resolve(id);   // F-18
        if (!_nodeOverrides.TryGetValue(id, out var l)) _nodeOverrides[id] = l = new List<string>();
        l.Add(json);
        Recompute();
    }

    /// <summary>F-8 — replaces the cluster override layer (folded from the master's broadcast). Ids this node does not have are ignored.</summary>
    public void SetClusterOverrides(IReadOnlyList<FeatureOverrideSpec> overrides)
    {
        _cluster = overrides ?? Array.Empty<FeatureOverrideSpec>();
        Recompute();
    }

    /// <summary>
    /// F-14 — a load starts: the AtLoad features freeze to the cluster layer the master stamped for this node (id → partial JSON; missing = no
    /// cluster override). Defaults, role default and node override still apply under it; later cluster changes do not reach an AtLoad feature
    /// until the next load. A previously recorded full config no longer applies.
    /// </summary>
    public void FreezeAtLoad(IReadOnlyDictionary<string, string>? clusterLayer)
    {
        _frozenFull.Clear();
        _frozenLayer.Clear();
        foreach (var d in _registry.All)
        {
            if (d.Change != FeatureChange.AtLoad) continue;
            string json = "{}";
            foreach (var o in _cluster)   // the node's own view of the layer, when the payload carries none (a node that never received one)
                if (clusterLayer == null && o.Feature == d.Id && (o.Nodes.Count == 0 || Contains(o.Nodes, _nodeName())))
                    json = FeatureJson.MergeFlat(json, o.Json);
            if (clusterLayer != null && clusterLayer.TryGetValue(d.Id, out var stamped)) json = stamped;
            _frozenLayer[d.Id] = json;
        }
        Recompute();
    }

    /// <summary>F-15/F-24 — a branch to live: the recorded FULL configs win for AtLoad features. THROWS on a feature this build does not declare, a Live one, or a key its type lost.</summary>
    public void FreezeRecorded(IReadOnlyDictionary<string, string> recorded)
    {
        var scratch = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in recorded)
        {
            if (!_registry.TryIndex(kv.Key, out int i))
                throw new InvalidOperationException($"The recording needs feature '{kv.Key}', which this build does not declare — the branch is refused (F-15).");
            var d = _registry.All[i];
            if (d.Change != FeatureChange.AtLoad)
                throw new InvalidOperationException($"The recording names '{kv.Key}' as an AtLoad feature, but this build declares it Live — the branch is refused.");
            try { d.Ops.Overlay(d.Ops.NewSlot(), kv.Value); }
            catch (JsonException e) { throw new InvalidOperationException($"The recorded config of feature '{kv.Key}' no longer fits its type: {e.Message} — the branch is refused (F-24)."); }
            scratch[kv.Key] = kv.Value;
        }
        _frozenLayer.Clear();
        _frozenFull.Clear();
        foreach (var kv in scratch) _frozenFull[kv.Key] = kv.Value;
        Recompute();
    }

    /// <summary>The effective FULL JSON of every AtLoad feature this node has (what the recording keeps).</summary>
    public Dictionary<string, string> AtLoadConfigs()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < _slots.Length; i++)
            if (_registry.All[i].Change == FeatureChange.AtLoad) d[_registry.All[i].Id] = _registry.All[i].Ops.ToJson(_slots[i]);
        return d;
    }

    IReadOnlyDictionary<string, string> IFeatureRecording.AtLoadConfigs() => AtLoadConfigs();

    /// <summary>Folds the master's broadcast (a NEW list per broadcast): recomputes only when the reference changed.</summary>
    public void SyncCluster(IReadOnlyList<FeatureOverrideSpec> fromView)
    {
        if (!ReferenceEquals(fromView, _cluster)) SetClusterOverrides(fromView);
    }

    /// <summary>The ids that are ON (heartbeat <c>FeaturesOn</c>).</summary>
    public List<string> OnSet()
    {
        var l = new List<string>();
        for (int i = 0; i < _on.Length; i++) if (_on[i]) l.Add(_registry.All[i].Id);
        return l;
    }

    private void Recompute()
    {
        _problems.Clear();
        string me = _nodeName();
        for (int i = 0; i < _slots.Length; i++)
        {
            var d = _registry.All[i];
            var slot = d.Ops.NewSlot();
            if (d.Change == FeatureChange.AtLoad && _frozenFull.TryGetValue(d.Id, out var full))
                Try(d, slot, full, "recording");
            else
            {
                Try(d, slot, d.DefaultFor(_role), "role default");
                if (_nodeOverrides.TryGetValue(d.Id, out var mine))
                    foreach (var j in mine) Try(d, slot, j, "node override");
                if (d.Change == FeatureChange.AtLoad && _frozenLayer.TryGetValue(d.Id, out var layer))
                    Try(d, slot, layer, "load");
                else if (d.Change == FeatureChange.Live || _frozenLayer.Count == 0)
                    foreach (var o in _cluster)
                        if (o.Feature == d.Id && (o.Nodes.Count == 0 || Contains(o.Nodes, me)))
                            Try(d, slot, o.Json, "cluster override");
            }
            _slots[i] = slot;
            _on[i] = d.Ops.IsEnabled(slot);
        }
        Changed?.Invoke();
    }

    private static bool Contains(IReadOnlyList<string> names, string me)
    {
        for (int i = 0; i < names.Count; i++) if (names[i] == me) return true;
        return false;
    }

    private void Try(FeatureDeclaration d, Array slot, string? json, string layer)
    {
        try { d.Ops.Overlay(slot, json); }
        catch (JsonException e) { _problems.Add($"{d.Id} ({layer}): {e.Message}"); }
    }
}

/// <summary>
/// ⭐ Node Features F-7 — the node override layer from <c>--feature</c>, parsed by the runner ONCE per process. Process-wide on purpose: every
/// host reaches the node features through the one shared bootstrapper, so none can forget to pass it (a per-host config field would be N wirings).
/// A node applies the entries whose feature it declares; the rest belong to another node of the process — and an id NO node declares is
/// refused at startup (<see cref="Unclaimed"/>).
/// </summary>
public static class NodeFeatureOverrides
{
    private static readonly List<(string Id, string Json)> _fromCommandLine = new();

    private static readonly HashSet<string> _declared = new(StringComparer.Ordinal);
    private static readonly object _gate = new();

    public static IReadOnlyList<(string Id, string Json)> FromCommandLine => _fromCommandLine;

    public static void Set(IEnumerable<(string Id, string Json)> overrides)
    {
        lock (_gate) { _fromCommandLine.Clear(); _fromCommandLine.AddRange(overrides); _declared.Clear(); }
    }

    /// <summary>F-18 — a node of this process built its registry: its declared ids can claim a <c>--feature</c> entry.</summary>
    public static void NoteDeclared(IEnumerable<string> ids)
    {
        lock (_gate) foreach (var id in ids) _declared.Add(id);
    }

    /// <summary>The <c>--feature</c> ids no node of this process declared (a typo). Call once every in-process node has built its registry; empty = fine.</summary>
    public static IReadOnlyList<string> Unclaimed()
    {
        lock (_gate)
        {
            var l = new List<string>();
            foreach (var (id, _) in _fromCommandLine) if (!_declared.Contains(id) && !l.Contains(id)) l.Add(id);
            return l;
        }
    }

    /// <summary>Parses one <c>--feature</c> value: <c>id=on|off</c> (short for <c>{"Enabled":…}</c>) or <c>id={json object}</c>. Throws on anything else.</summary>
    public static (string Id, string Json) Parse(string text)
    {
        int eq = text?.IndexOf('=') ?? -1;
        if (eq <= 0) throw new FormatException($"--feature '{text}': expected id=on|off or id={{json}}.");
        string id = text!.Substring(0, eq).Trim(), v = text.Substring(eq + 1).Trim();
        if (v.Equals("on", StringComparison.OrdinalIgnoreCase)) return (id, "{\"Enabled\":true}");
        if (v.Equals("off", StringComparison.OrdinalIgnoreCase)) return (id, "{\"Enabled\":false}");
        if (v.StartsWith("{"))
        {
            try { if (System.Text.Json.Nodes.JsonNode.Parse(v) is not System.Text.Json.Nodes.JsonObject) throw new FormatException(); }
            catch (JsonException e) { throw new FormatException($"--feature '{text}': {e.Message}"); }
            return (id, v);
        }
        throw new FormatException($"--feature '{text}': the value is on | off | a JSON object.");
    }
}
