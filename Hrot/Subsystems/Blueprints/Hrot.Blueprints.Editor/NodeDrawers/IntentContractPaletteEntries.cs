using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>One declared behaviour params contract (<c>[Behavior(Params = …)]</c>, CE-2134): its id, DTO FQN and the members a pin can carry.</summary>
public sealed record IntentContract(string Id, string DtoTypeFqn, IReadOnlyList<StructFieldDecl> Members);

/// <summary>
/// ⭐ <c>CE-472</c> — discovers the <c>[Behavior(Params = …)]</c> parameter DTOs the Send Intent / To JSON / From JSON palette
/// offers. Mirrors <see cref="ISharedStructTypeProvider"/>. 📄 <c>docs/blueprints/DESIGN_Typed_Intent_And_Json_Nodes.md</c>.
/// </summary>
public interface IIntentContractProvider
{
    IReadOnlyList<IntentContract> GetContracts();
}

/// <summary>
/// Default <see cref="IIntentContractProvider"/>: every behaviour's declared params DTO from the ONE scan,
/// <c>Hrot.Map.Definitions.Behavior.ParamsDtoRegistry</c> (<c>CE-2134</c>). ⛔ HISTORY: it scanned the loaded assemblies itself
/// for an attribute NAMED <c>BehaviorContractAttribute</c>, believing <c>Hrot.Core</c> unreachable — it is reachable through
/// <c>Hrot.Editor.AiShared</c>, and a second scan of one attribute is what ruling 9 forbids.
/// </summary>
public sealed class ReflectionIntentContractProvider : IIntentContractProvider
{
    private IReadOnlyList<IntentContract>? _cached;
    private Hrot.Map.Definitions.Behavior.ParamsDtoRegistry? _cachedFor;

    /// <summary>⭐ <c>CE-3186</c> (P2) — recomputed whenever <c>ParamsDtoRegistry.Default</c> is a new instance (it drops itself on every
    /// assembly load), so a rebuilt DTO reaches the palette; the cache never outlives the registry it was built from.</summary>
    public IReadOnlyList<IntentContract> GetContracts()
    {
        var registry = Hrot.Map.Definitions.Behavior.ParamsDtoRegistry.Default;
        if (_cached == null || !ReferenceEquals(_cachedFor, registry)) { _cached = FromRegistry(registry); _cachedFor = registry; }
        return _cached;
    }

    /// <summary>Scans <paramref name="assemblies"/> (exposed so a rail can scan a known assembly set).</summary>
    public static IReadOnlyList<IntentContract> Compute(IEnumerable<Assembly> assemblies)
        => FromRegistry(Hrot.Map.Definitions.Behavior.ParamsDtoRegistry.Scan(assemblies));

    /// <summary>The contracts <paramref name="registry"/> declares, by behaviour id.</summary>
    public static IReadOnlyList<IntentContract> FromRegistry(Hrot.Map.Definitions.Behavior.ParamsDtoRegistry registry)
    {
        var result = new List<IntentContract>();
        foreach (var d in registry.Behaviors)
        {
            var t = d.ParamsType;
            // ⭐ CE-2023 ③ ("S8n") — a STRUCT contract too (MoveToLocation / FireAtTarget are structs so a host can bind them).
            if (!(t.IsClass || (t.IsValueType && !t.IsEnum && !t.IsPrimitive)) || t.IsAbstract
                || t.FullName is not { Length: > 0 } fqn) continue;
            if (t.IsClass && t.GetConstructor(Type.EmptyTypes) is null) continue;   // From JSON / new T() need one (a struct always has it)
            result.Add(new IntentContract(d.Name, fqn.Replace('+', '.'), PinnableMembers(t)));
        }
        return result.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The members a pin can carry: public, readable AND writable, not <c>[JsonIgnore]</c>, of a primitive, string or
    /// enum type, or an <c>EntityRef</c>. ⚠ Anything else (e.g. <c>PickableGeoPoint</c>) is left out and keeps the DTO's own default.
    /// </summary>
    public static IReadOnlyList<StructFieldDecl> PinnableMembers(Type t)
    {
        var members = new List<StructFieldDecl>();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.SetMethod is not { IsPublic: true } || p.GetIndexParameters().Length > 0) continue;
            if (IsJsonIgnored(p) || !IsPinnable(p.PropertyType)) continue;
            members.Add(new StructFieldDecl { Name = p.Name, TypeId = PinTypeId(p.PropertyType) });
        }
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.IsInitOnly || IsJsonIgnored(f) || !IsPinnable(f.FieldType)) continue;
            members.Add(new StructFieldDecl { Name = f.Name, TypeId = PinTypeId(f.FieldType) });
        }
        return members;
    }

    private static bool IsJsonIgnored(MemberInfo m)
        => m.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.Text.Json.Serialization.JsonIgnoreAttribute");

    private static bool IsPinnable(Type type)
        => type.IsPrimitive || type.IsEnum || type == typeof(string)
           || type == typeof(Fdp.Toolkit.Replication.EntityRef);   // ⭐ DESIGN_Entity_Reference D6 — a reference pin

    /// <summary>An enum pin is spelled <c>global::Ns.Enum</c> (a bare FQN is <c>BP1500</c>) — the <c>NodePinSchema</c> rule.</summary>
    private static string PinTypeId(Type type)
        => type.IsEnum ? "global::" + (type.FullName ?? type.Name).Replace('+', '.') : type.FullName ?? type.Name;
}

/// <summary>⭐ <c>CE-472</c> — one Send Intent row per declared behaviour, and ONE To JSON / From JSON pair per DTO TYPE
/// (<c>CE-2134</c>: several behaviours may share a DTO, and its JSON rows are keyed by the type, so they are listed once).</summary>
public static class IntentContractPaletteEntries
{
    public static IEnumerable<NodeKindDescriptor> Entries(IIntentContractProvider provider)
    {
        if (provider is null) yield break;
        var jsonRowsFor = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in provider.GetContracts())
        {
            var shortName = c.DtoTypeFqn.Substring(c.DtoTypeFqn.LastIndexOf('.') + 1);
            yield return new NodeKindDescriptor
            {
                Kind        = $"Intent.Send.{c.Id}",
                DisplayName = $"Send Intent: {c.Id}",
                Category    = "Intent",
                Tooltip     = $"Publish the '{c.Id}' intent to an entity (default self); its parameters are {shortName}.",
                Icon        = "bp/function",
                CreateInstance = () => new SendIntentNode { Id = Guid.NewGuid(), IntentId = c.Id, DtoTypeFqn = c.DtoTypeFqn, Fields = Copy(c) },
            };
            if (!jsonRowsFor.Add(c.DtoTypeFqn)) continue;   // a shared DTO: its To / From JSON rows are already listed
            yield return new NodeKindDescriptor
            {
                Kind        = $"Json.To.{c.DtoTypeFqn}",
                DisplayName = $"To JSON [{shortName}]",
                Category    = "JSON",
                Tooltip     = $"Serialise {shortName} members to a JSON string (the behaviour-parse settings).",
                Icon        = "bp/function",
                CreateInstance = () => new ToJsonNode { Id = Guid.NewGuid(), DtoTypeFqn = c.DtoTypeFqn, Fields = Copy(c) },
            };
            yield return new NodeKindDescriptor
            {
                Kind        = $"Json.From.{c.DtoTypeFqn}",
                DisplayName = $"From JSON [{shortName}]",
                Category    = "JSON",
                Tooltip     = $"Parse a JSON string into {shortName} members; Ok is false (members default) on bad input.",
                Icon        = "bp/function",
                CreateInstance = () => new FromJsonNode { Id = Guid.NewGuid(), DtoTypeFqn = c.DtoTypeFqn, Fields = Copy(c) },
            };
        }
    }

    private static List<StructFieldDecl> Copy(IntentContract c)
        => c.Members.Select(m => new StructFieldDecl { Name = m.Name, TypeId = m.TypeId }).ToList();
}
