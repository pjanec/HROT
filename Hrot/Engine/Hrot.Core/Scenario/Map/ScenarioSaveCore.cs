using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Serialization;
using Fdp.Core.Serialization;

namespace Hrot.Map.Common.Scenario;

/// <summary>
/// ⭐⭐⭐ <b>The ONE scenario-save implementation — host-neutral, used identically by every host.</b>
///
/// <para>There is NO editor-specific scenario save. Whether a save is triggered from the editor UI, from CGF,
/// from a SimHost or IG node, or from the MCP debug API, the bytes are produced HERE: the gated
/// <see cref="ScenarioSerializer"/> (CE-275 ②: <c>save entity ⇔ IsPrimaryOwner AND NOT ScenarioIgnoreTag</c>)
/// stamps <c>$meta</c> and writes the owned entity slice. <c>HrotScenarioSaveHandler</c> (the cluster
/// fan-out handler on every node) calls this, and the editor's thin <c>ScenarioFileService</c> shim calls
/// the very same method — one code path, no duplication.</para>
///
/// <para>⛔ <b>It no longer attaches a <c>Zones</c> section (F1, retired 2026-09-17).</b> It used to take an
/// <c>IZoneManagerService</c> and append that host's active zone DTOs to the DOM. A zone is now an ordinary
/// authored ENTITY, so the gated serialization above already carries it — appending a second representation
/// would be two producers for one slot (<c>R-132</c>), which is precisely what the retirement removes.
/// ⚠ Nothing was lost: what the section described (road networks) is now declared by the TERRAIN asset, and
/// what it listed (obstacles) is now entities.
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.1, §6.</para>
///
/// <para>⭐ It depends only on low, shared types (<see cref="ScenarioSerializer"/> in Fdp.Toolkits and the
/// JSON options in Hrot.Core), so it lives at the shared engine layer and nothing about it is
/// "presentation" or "editor".</para>
///
/// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4.
/// </summary>
public static class ScenarioSaveCore
{
    /// <summary>
    /// ⭐ CE-4210 MA-4 — THE save header, built from what the WORLD holds: its terrain (CE-3015; ⛔ R-4018 never a TKB name), its description (CE-3288) and the
    /// MACRO header fields the load stamped resident (<c>Kind</c>, <c>Params</c>, <c>Anchor</c>, <c>Placement.MaxShift</c>, <c>Relocatable</c>). Both save sites call this
    /// — <c>HrotScenarioSaveHandler</c> (every host's distributed save) and <c>ScenarioFileService</c> (the editor's shim) — so no host can forget a field, and
    /// opening a macro and saving it no longer turns it into a plain scenario.
    /// </summary>
     public static ScenarioHeader HeaderOf(EntityRepository world, string docType)
        => HeaderOf(world, docType, macroHeaderJson: null);

    /// <summary>
    /// ⭐ CE-4210 MA-9 — as above; with <paramref name="macroHeaderJson"/> (the object <see cref="ScenarioHeaderJson.Write"/> produces — Kind, Params, Anchor, Placement, Relocatable) the macro fields come from IT, not from the
    /// resident (Save selection as macro: the selection's own header), and the source scenario's description is NOT carried into the macro. Null / blank = from the resident, exactly as before. Terrain stays the resident's.
    /// </summary>
    public static ScenarioHeader HeaderOf(EntityRepository world, string docType, string? macroHeaderJson)
    {
        var over = HeaderFromJson(macroHeaderJson);
        var resident = over ?? ScenarioDescription.HeaderOf(world);
        return new ScenarioHeader(docType,
            TerrainName: Fdp.Toolkit.Terrain.TerrainDefinition.ResidentName(world),
            Description: over is null ? ScenarioDescription.ResidentOf(world) : null)
        {
            Kind        = resident?.Kind,
            Params      = resident?.Params,
            Anchor      = resident?.Anchor,
            MaxShift    = resident?.MaxShift,
            Relocatable = resident?.Relocatable ?? true,
            // ⭐ CE-3375 — the presets and (found the same day, P-7) the feature preset are the SCENARIO's: an edit save keeps them. ⛔ They were not copied, so a
            //   save dropped header.Features. A macro header (Save selection as macro) carries neither.
            Features    = over is null ? resident?.Features : null,
            Presets     = over is null ? resident?.Presets : null,
        };
    }

    /// <summary>The macro header fields a header JSON object holds (null for none / unreadable).</summary>
    public static ScenarioHeader? HeaderFromJson(string? macroHeaderJson)
    {
        if (string.IsNullOrWhiteSpace(macroHeaderJson)) return null;
        try { return ScenarioHeaderJson.Read(new JsonObject { ["Header"] = JsonNode.Parse(macroHeaderJson) }); }
        catch (JsonException) { return null; }
    }

    public static JsonObject BuildDom(
        ScenarioSerializer serializer,
        EntityRepository   world,
        ScenarioHeader     header)
        => BuildDom(serializer, world, header, onlyGuids: null);

    /// <summary>
    /// ⭐ CE-4210 MA-9 — the DOM of a SELECTION: only the entities whose <c>EntityGuid</c> is in <paramref name="onlyGuids"/> (null / empty = everything, as before), and — when the header's params bind outside references —
    /// those references rewritten to their <c>^param</c> form IN THE DOM ONLY (the world is never touched).
    /// </summary>
    public static JsonObject BuildDom(
        ScenarioSerializer serializer,
        EntityRepository   world,
        ScenarioHeader     header,
        IReadOnlyCollection<Guid>? onlyGuids,
        ScenarioBehaviorRemapper? behaviorRemapper = null)
    {
        var set = onlyGuids is { Count: > 0 } ? new HashSet<Guid>(onlyGuids) : null;
        var dom = serializer.Serialize(world, header, set);
        if (set is not null) RebindOutsideReferences(dom, header, world, serializer, behaviorRemapper);
        return dom;
    }

    /// <summary>
    /// ⭐⭐ CE-4210 MA-9 — a macro is CLOSED (SR-58): every reference from the saved entities to one outside the selection became a header param whose DEFAULT names that outside entity — its key
    /// (<c>"@Key"</c>) or its GUID. This rewrites each such reference to its <c>"^name"</c> Param form IN THE DOM ONLY (the world is never touched), TYPED: the S0 walker
    /// (<see cref="EntityRefRemap.WalkJson"/>) visits only the <see cref="EntityLink"/> / <see cref="AreaLink"/> leaves of each saved component — a plain number or string that merely equals a network id never changes.
    /// A reference matches a param by the authored form it carries (key / GUID) OR by the network id of the entity the param's default names in <paramref name="world"/> (how an unauthored runtime reference is written).
    /// Params that are not references are ignored. Returns the number of components rewritten.
    /// </summary>
    public static int RebindOutsideReferences(JsonObject dom, ScenarioHeader header, EntityRepository? world, ScenarioSerializer serializer, ScenarioBehaviorRemapper? behaviorRemapper = null)
    {
        if (header.Params is not { Count: > 0 } ps || dom["Entities"] is not JsonObject entities) return 0;
        var targets = new List<RebindTarget>();
        foreach (var p in ps)
            if (p.TypeId is "EntityLink" or "AreaLink" && p.Default is JsonValue v && v.TryGetValue<string>(out var text) && text.Length > 0)
                targets.Add(TargetOf(world, p.Name, text));
        if (targets.Count == 0) return 0;

        var typeByName = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var t in ComponentTypeRegistry.GetAllTypes())
            if (EntityRefRemap.HoldsLeaves(t) && serializer.AutoSerializer.GetComponentName(ComponentTypeRegistry.GetId(t)) is { } name) typeByName[name] = t;

        var applier = new RebindApplier(targets);
        int n = 0;
        foreach (var (_, ent) in entities)
        {
            if (ent is not JsonObject components) continue;
            foreach (var key in components.Select(kv => kv.Key).ToList())
            {
                if (behaviorRemapper is not null && RebindBlobs(key, components[key], behaviorRemapper, applier, out bool blobChanged)) { if (blobChanged) n++; continue; }
                if (!typeByName.TryGetValue(key, out var type) || components[key] is not JsonObject comp) continue;
                string before = comp.ToJsonString();
                string? after = EntityRefRemap.WalkJson(type, before, applier);
                if (after is not null && after != before) { components[key] = JsonNode.Parse(after); n++; }
            }
        }
        return n;
    }

    /// <summary>
    /// ⭐⭐ CE-4210 W-M6 — the params BLOBS of a saved entity (SR-159: a reference inside params is bound as its Param form, like a typed leaf): the brain slots <c>Behavior</c> / <c>Sop</c> (<c>{Name, Params, Origin}</c>, <c>Params</c> a JSON string)
    /// and the mission plan's tasks (<c>MissionPlan.PlanData.tasks[].behaviorName / behaviorParams</c>), each walked through the behaviour's own contract by <see cref="ScenarioBehaviorRemapper.ApplyJson"/> — the ONE blob parser
    /// (the very call <see cref="ScenarioEntityWalk"/> makes on the live side). ⛔ <c>ParamBindings</c> strings are bindings, not references: not a blob here, left as saved. Returns true when <paramref name="key"/> named a blob holder.
    /// </summary>
    private static bool RebindBlobs(string key, JsonNode? node, ScenarioBehaviorRemapper remapper, LeafApplier applier, out bool changed)
    {
        changed = false;
        switch (key)
        {
            case "Behavior" or "Sop" when node is JsonObject slot:
                changed = RebindBlob(slot, "Name", "Params", remapper, applier);
                return true;
            case "MissionPlan" when node is JsonObject plan:
                if (plan["PlanData"]?["tasks"] is JsonArray tasks)
                    foreach (var task in tasks)
                        if (task is JsonObject t) changed |= RebindBlob(t, "behaviorName", "behaviorParams", remapper, applier);
                return true;
            default:
                return false;
        }
    }

    private static bool RebindBlob(JsonObject holder, string nameKey, string paramsKey, ScenarioBehaviorRemapper remapper, LeafApplier applier)
    {
        if (holder[nameKey] is not JsonValue nv || !nv.TryGetValue<string>(out var name)) return false;
        // ⭐ CE-4295 — the params are an object in a new file ([JsonText]), an escaped string in an old one: both are walked, each written back in its own form
        var held = holder[paramsKey];
        string? json = Fdp.Core.Serialization.Converters.JsonTextConverter.TextOf(held);
        if (json is null) return false;
        var after = remapper.ApplyJson(name, json, applier);
        if (after is null || after == json) return false;
        holder[paramsKey] = held is JsonValue ? JsonValue.Create(after) : Fdp.Core.Serialization.Converters.JsonTextConverter.NodeOf(after);
        return true;
    }

    private sealed record RebindTarget(string Param, string Text, int NetworkId, string? Guid);

    private static RebindTarget TargetOf(EntityRepository? world, string param, string text)
    {
        int netId = 0; string? guid = null;
        if (world is not null)
        {
            Entity found = Entity.Null;
            if (text[0] == '@' && world.IsComponentTypeRegistered<RefKey>())
            {
                foreach (var e in world.Query().With<RefKey>().Build())
                    if (world.GetComponent<RefKey>(e).Value.ToString() == text.Substring(1)) { found = e; break; }
            }
            else if (Guid.TryParse(text, out var g) && world.IsComponentTypeRegistered<EntityGuid>())
            {
                foreach (var e in world.Query().With<EntityGuid>().Build())
                    if (world.GetComponent<EntityGuid>(e).Value == g) { found = e; break; }
            }
            if (!found.IsNull)
            {
                netId = Fdp.Toolkit.Replication.Services.NetworkIdResolver.RuntimeNetworkIdOf(world, found);
                if (world.IsComponentTypeRegistered<EntityGuid>() && world.HasComponent<EntityGuid>(found)) guid = world.GetComponent<EntityGuid>(found).Value.ToString();
            }
        }
        return new RebindTarget(param, text, netId, guid);
    }

    private sealed class RebindApplier : LeafApplier
    {
        private readonly List<RebindTarget> _targets;
        public RebindApplier(List<RebindTarget> targets) => _targets = targets;

        private string? Match(EntityRef reference, RefSpec spec)
        {
            if (spec.Form == RefForm.Param) return null;
            foreach (var t in _targets)
            {
                if (t.NetworkId != 0 && reference.NetworkId == t.NetworkId) return t.Param;
                if (!spec.IsNone && (spec.ToString() == t.Text || spec.ToString() == t.Guid)) return t.Param;
            }
            return null;
        }

        // ⭐ the saved macro holds ONLY the binding (`"^name"`): the live answer (a network id of the SOURCE world) is dropped — it would dangle in the file, and the binder fills it on every load
        public override EntityLink Link(EntityLink l) => Match(l.Ref, l.Spec) is { } p ? new EntityLink(default, RefSpec.ByParam(p)) : l;
        public override AreaLink Area(AreaLink a) => a.IsShape || Match(a.Area, a.Spec) is not { } p ? a : a.WithArea(default).BindToParam(p);
    }

    public static void Write(
        ScenarioSerializer serializer,
        EntityRepository   world,
        string             filePath,
        ScenarioHeader     header)
        => Write(serializer, world, filePath, header, onlyGuids: null);

    public static void Write(
        ScenarioSerializer serializer,
        EntityRepository   world,
        string             filePath,
        ScenarioHeader     header,
        IReadOnlyCollection<Guid>? onlyGuids,
        ScenarioBehaviorRemapper? behaviorRemapper = null)
    {
        var dom = BuildDom(serializer, world, header, onlyGuids, behaviorRemapper);

        // CE-3187 (R-2005): a DOM ignores the naming policy, so the shared minified options are byte-identical.
        var json     = JsonSerializer.Serialize(dom, FdpJsonOptionsRegistry.DefaultRelaxed);
        File.WriteAllText(filePath, JsonAestheticFormatter.FlattenNumericArrays(json));
    }
}
