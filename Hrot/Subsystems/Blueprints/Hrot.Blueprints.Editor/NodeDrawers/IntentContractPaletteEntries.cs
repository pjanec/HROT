using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>One discovered <c>[BehaviorContract]</c> parameter DTO: its id, FQN and the members a pin can carry.</summary>
public sealed record IntentContract(string Id, string DtoTypeFqn, IReadOnlyList<StructFieldDecl> Members);

/// <summary>
/// ⭐ <c>CE-472</c> — discovers the <c>[BehaviorContract]</c> parameter DTOs the Send Intent / To JSON / From JSON palette
/// offers. Mirrors <see cref="ISharedStructTypeProvider"/>. 📄 <c>docs/blueprints/DESIGN_Typed_Intent_And_Json_Nodes.md</c>.
/// </summary>
public interface IIntentContractProvider
{
    IReadOnlyList<IntentContract> GetContracts();
}

/// <summary>
/// Default <see cref="IIntentContractProvider"/>: scans loaded assemblies for classes carrying an attribute NAMED
/// <c>BehaviorContractAttribute</c> and reads its first constructor argument (the id). ⚠ Matched by name, not by
/// type: the blueprint editor does not reference <c>Hrot.Core</c>, which defines it — the same reason
/// <see cref="ReflectionSharedStructTypeProvider"/> scans rather than links.
/// </summary>
public sealed class ReflectionIntentContractProvider : IIntentContractProvider
{
    private IReadOnlyList<IntentContract>? _cached;

    public IReadOnlyList<IntentContract> GetContracts() => _cached ??= Compute(AppDomain.CurrentDomain.GetAssemblies());

    /// <summary>Scans <paramref name="assemblies"/> (exposed so a rail can scan a known assembly set).</summary>
    public static IReadOnlyList<IntentContract> Compute(IEnumerable<Assembly> assemblies)
    {
        var result = new List<IntentContract>();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
            catch { continue; }

            foreach (var t in types)
            {
                if (!t.IsClass || t.IsAbstract || t.FullName is not { Length: > 0 } fqn) continue;
                if (t.GetConstructor(Type.EmptyTypes) is null) continue;   // From JSON / new T() need one
                var attr = t.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.Name == "BehaviorContractAttribute");
                if (attr is null || attr.ConstructorArguments.Count == 0 || attr.ConstructorArguments[0].Value is not string id
                    || id.Length == 0) continue;
                result.Add(new IntentContract(id, fqn.Replace('+', '.'), PinnableMembers(t)));
            }
        }
        return result
            .GroupBy(c => c.Id, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The members a pin can carry: public, readable AND writable, not <c>[JsonIgnore]</c>, of a primitive, string or
    /// enum type. ⚠ Anything else (e.g. <c>PickableGeoPoint</c>) is left out and keeps the DTO's own default.
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

    private static bool IsPinnable(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string);

    /// <summary>An enum pin is spelled <c>global::Ns.Enum</c> (a bare FQN is <c>BP1500</c>) — the <c>NodePinSchema</c> rule.</summary>
    private static string PinTypeId(Type type)
        => type.IsEnum ? "global::" + (type.FullName ?? type.Name).Replace('+', '.') : type.FullName ?? type.Name;
}

/// <summary>⭐ <c>CE-472</c> — one Send Intent / To JSON / From JSON palette row per discovered contract.</summary>
public static class IntentContractPaletteEntries
{
    public static IEnumerable<NodeKindDescriptor> Entries(IIntentContractProvider provider)
    {
        if (provider is null) yield break;
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
