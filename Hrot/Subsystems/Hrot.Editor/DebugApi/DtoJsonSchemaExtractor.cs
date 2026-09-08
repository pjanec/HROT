using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// Turns a parameter DTO into a JSON schema an agent can author against — the shared half of
    /// <c>MX4a</c> (behaviour params) and <c>MX7</c> (breakpoint conditions).
    ///
    /// <para><b>One extractor, two callers, on purpose.</b> Both questions reduce to "reflect a
    /// DTO's public properties into a schema": behaviour params and predicate arms differ only in
    /// where the type list comes from. A second walk would be a second set of type mappings to keep
    /// in step, and they would drift.</para>
    ///
    /// <para><b>It describes the shape the ENGINE actually parses.</b> Predicate arms round-trip
    /// through the existing <c>SearchPredicateJsonOptions</c>, so the schema names the same
    /// <c>$type</c> discriminator and the same property names STJ binds — ⛔ no new encoding is
    /// introduced anywhere.</para>
    ///
    /// <para><b>Deliberately not a JSON-Schema implementation.</b> The output is the useful subset —
    /// <c>type</c>, <c>enum</c>, <c>properties</c>, plus the picker hints the editor already declares
    /// via attributes. An agent needs to know a field's name, its type, and whether it is an entity
    /// reference or a property path; a spec-complete emitter would be more code and no more
    /// answerable.</para>
    /// </summary>
    internal static class DtoJsonSchemaExtractor
    {
        /// <summary>Sentinel used for a nested predicate: the value is another arm of the union.</summary>
        private const string PredicateUnionRef = "SearchPredicateDto";

        /// <summary>
        /// The schema of one parameter DTO: <c>{ type:"object", properties:{ name: {...} } }</c>.
        /// Returns an empty object schema for a behaviour that takes no parameters — ⛔ never null,
        /// so a caller never has to distinguish "no params" from "unknown".
        /// </summary>
        public static JsonObject ExtractParams(Type? dtoType)
        {
            var properties = new JsonObject();

            if (dtoType is not null)
            {
                foreach (var member in PublicWritableMembers(dtoType))
                    properties[member.Name] = DescribeMember(member);
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
            };
        }

        /// <summary>
        /// Every registered arm of the <see cref="SearchPredicateDto"/> polymorphic union, each with
        /// its <c>$type</c> discriminator and its parameter schema.
        ///
        /// <para><b>The union is CLOSED and self-declaring</b> — the arms are exactly the
        /// <see cref="JsonDerivedTypeAttribute"/>s on the base type, so this cannot fall out of step
        /// with what the deserializer accepts: both read the same attributes. ⚠ <c>EnumPredicateDto</c>
        /// is intentionally absent (it is generic and therefore not registrable).</para>
        /// </summary>
        public static JsonArray ExtractPredicateUnion()
        {
            var arms = new JsonArray();

            foreach (var derived in typeof(SearchPredicateDto)
                         .GetCustomAttributes<JsonDerivedTypeAttribute>()
                         .OrderBy(a => a.TypeDiscriminator?.ToString(), StringComparer.Ordinal))
            {
                if (derived.TypeDiscriminator is not string discriminator)
                    continue;

                arms.Add(new JsonObject
                {
                    ["$type"] = discriminator,
                    ["clrType"] = derived.DerivedType.Name,
                    ["paramSchema"] = ExtractParams(derived.DerivedType),
                });
            }

            return arms;
        }

        /// <summary>
        /// One fillable member of a DTO — a public read-write property <b>or</b> a public instance field.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b><c>CE-224</c> — WHY FIELDS ARE HERE, and it is not a generalisation for its own sake.</b>
        /// This extractor originally read <c>GetProperties()</c> alone, which is correct for the
        /// breakpoint-predicate DTOs (ordinary classes with properties) and <b>silently wrong for every
        /// behaviour parameter DTO</b>. A behaviour's params struct is memcpy'd into
        /// <c>BrainBlackboard.BehaviorParameters</c>, a fixed byte region with a hard size cap, so it is
        /// declared <c>[StructLayout(LayoutKind.Sequential)]</c> with public <b>fields</b> —
        /// <c>MoveToLocationParams { public float X; public float Y; public float Speed;
        /// public float ArrivalRadius; }</c>. Properties would put the layout at the compiler's
        /// discretion and break that contract, so the DTOs cannot be reshaped to suit the extractor;
        /// the extractor has to read what they actually are.
        ///
        /// <para>📐 Measured on the live editor, <c>2026-09-08</c>: <c>GET /behaviors</c> returned
        /// <c>properties:{}</c> for all 40 behaviours while <c>GET /breakpoint-types</c> returned full
        /// schemas from the same code path — the two differ only in property-vs-field.</para>
        /// </remarks>
        private readonly record struct SchemaMember(string Name, Type Type, MemberInfo Member);

        /// <summary>
        /// Every member an agent may fill in: public read-write properties, then public instance fields.
        /// </summary>
        /// <remarks>
        /// ⚠ <b>Excluded on purpose:</b> <c>const</c> (<c>IsLiteral</c>) and <c>readonly</c>
        /// (<c>IsInitOnly</c>) fields — a caller cannot set either, so advertising them would invite a
        /// write that silently does nothing, which is the shape of defect this whole endpoint exists to
        /// prevent. Compiler-generated property backing fields are private and so never reach here.
        ///
        /// <para>⚠ The order is properties-then-fields, each in reflection order. That is deterministic
        /// but it is <b>not</b> a wire order: the params region is filled by the behaviour's own
        /// resolver, never by field position from this schema.</para>
        /// </remarks>
        private static IEnumerable<SchemaMember> PublicWritableMembers(Type type)
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                    yield return new SchemaMember(p.Name, p.PropertyType, p);

            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (!f.IsInitOnly && !f.IsLiteral)
                    yield return new SchemaMember(f.Name, f.FieldType, f);
        }

        private static JsonObject DescribeMember(SchemaMember member)
        {
            var schema = Describe(member.Type);

            // The editor already declares these on the very same members; surfacing them is what
            // lets an agent tell "a string" from "a property path it must discover".
            foreach (var attribute in member.Member.GetCustomAttributes())
            {
                switch (attribute.GetType().Name)
                {
                    case "PropertyPathPickerAttribute":       schema["picker"] = "propertyPath"; break;
                    case "MapPickableEntityAttribute":        schema["picker"] = "entity"; break;
                    case "MapPickableWorldLocationAttribute": schema["picker"] = "worldLocation"; break;
                    case "MapPickableBoundingBoxAttribute":   schema["picker"] = "boundingBox"; break;
                    case "RemapNetworkIdAttribute":           schema["remapNetworkId"] = true; break;
                }
            }

            return schema;
        }

        private static JsonObject Describe(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying is not null)
            {
                var inner = Describe(underlying);
                inner["nullable"] = true;
                return inner;
            }

            if (type.IsEnum)
            {
                var values = new JsonArray();
                foreach (var name in Enum.GetNames(type)) values.Add(name);
                return new JsonObject { ["type"] = "string", ["enum"] = values };
            }

            // A nested predicate is another arm of the same union — say so by reference rather than
            // inlining, which would recurse forever on Compound.
            if (typeof(SearchPredicateDto).IsAssignableFrom(type))
                return new JsonObject { ["type"] = "object", ["$ref"] = PredicateUnionRef };

            if (type == typeof(Type))
            {
                // Serialized by TypeNameJsonConverter as the bare type NAME, not an assembly-qualified
                // name — an agent that sent the latter would be silently rejected.
                return new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "componentOrEventTypeName",
                    ["seeEndpoint"] = "GET /components",
                };
            }

            if (type == typeof(Vector3))
                return new JsonObject { ["type"] = "object", ["format"] = "vector3", ["properties"] = XyzProperties() };

            if (type.IsArray)
                return new JsonObject { ["type"] = "array", ["items"] = Describe(type.GetElementType()!) };

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return new JsonObject { ["type"] = "array", ["items"] = Describe(type.GetGenericArguments()[0]) };

            if (type == typeof(string))  return new JsonObject { ["type"] = "string" };
            if (type == typeof(bool))    return new JsonObject { ["type"] = "boolean" };
            if (type == typeof(Guid))    return new JsonObject { ["type"] = "string", ["format"] = "guid" };

            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
                return new JsonObject { ["type"] = "number" };

            if (type == typeof(int) || type == typeof(long) || type == typeof(short)
                || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong)
                || type == typeof(ushort) || type == typeof(sbyte))
                return new JsonObject { ["type"] = "integer" };

            // Anything else is a struct/class the agent still has to fill in, so describe its own
            // properties one level down rather than emitting a useless "object".
            if (!type.IsPrimitive && (type.IsClass || type.IsValueType))
            {
                var nested = new JsonObject();
                foreach (var member in PublicWritableMembers(type))
                    nested[member.Name] = Describe(member.Type);

                var schema = new JsonObject { ["type"] = "object", ["clrType"] = type.Name };
                if (nested.Count > 0) schema["properties"] = nested;
                return schema;
            }

            return new JsonObject { ["type"] = "object", ["clrType"] = type.Name };
        }

        private static JsonObject XyzProperties() => new()
        {
            ["X"] = new JsonObject { ["type"] = "number" },
            ["Y"] = new JsonObject { ["type"] = "number" },
            ["Z"] = new JsonObject { ["type"] = "number" },
        };
    }
}
