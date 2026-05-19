using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// Compiles per-field setter delegates for <see cref="DebugState"/> at startup
    /// (zero runtime reflection on the hot path) and applies JSON patches to a
    /// <c>ref DebugState</c> at call time.
    /// </summary>
    /// <remarks>
    /// Patch JSON shape: top-level property names match field names on <see cref="DebugState"/>.
    /// For <c>[Flags]</c> enum fields, the value is a nested object whose properties
    /// are enum-member names mapped to booleans (true sets the bit, false clears it).
    /// For primitive fields the value is a direct literal.
    /// </remarks>
    public delegate void DebugStateSetter(ref DebugState state, JsonElement element);

    public static class DebugStatePatchCompiler
    {
        private static readonly Dictionary<string, DebugStateSetter> _setters
            = new(StringComparer.Ordinal);

        private static bool _built;

        /// <summary>
        /// Compile all setter delegates. Idempotent — subsequent calls are no-ops.
        /// </summary>
        public static void Build()
        {
            if (_built) return;
            _built = true;

            var stateParam   = Expression.Parameter(typeof(DebugState).MakeByRefType(), "state");
            var elementParam = Expression.Parameter(typeof(JsonElement), "element");

            foreach (var field in typeof(DebugState).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var fieldAccess = Expression.Field(stateParam, field);

                if (field.FieldType.IsEnum &&
                    field.FieldType.GetCustomAttribute<FlagsAttribute>() != null)
                {
                    _setters[field.Name] = CompileFlagsPatcher(field.FieldType, fieldAccess, stateParam, elementParam);
                }
                else
                {
                    _setters[field.Name] = CompilePrimitivePatcher(field.FieldType, fieldAccess, stateParam, elementParam);
                }
            }
        }

        /// <summary>
        /// Apply a JSON patch to a debug-state instance. Unknown top-level properties
        /// are silently ignored (forward-compatibility). Malformed JSON throws
        /// <see cref="JsonException"/>.
        /// </summary>
        public static void ApplyPatch(ref DebugState state, string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            if (!_built) Build();

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (_setters.TryGetValue(prop.Name, out var setter))
                    setter(ref state, prop.Value);
            }
        }

        private static DebugStateSetter CompileFlagsPatcher(
            Type enumType,
            Expression fieldAccess,
            ParameterExpression stateParam,
            ParameterExpression elementParam)
        {
            // Resolve helpers
            var tryGetPropMethod = typeof(JsonElement).GetMethod(
                nameof(JsonElement.TryGetProperty),
                new[] { typeof(string), typeof(JsonElement).MakeByRefType() })!;
            var getBoolMethod    = typeof(JsonElement).GetMethod(nameof(JsonElement.GetBoolean), Type.EmptyTypes)!;

            // The enum's underlying integer type drives the bitwise ops.
            var underlying = Enum.GetUnderlyingType(enumType);

            var propElementVar = Expression.Variable(typeof(JsonElement), "propElement");
            var statements     = new List<Expression>();

            foreach (var enumName in Enum.GetNames(enumType))
            {
                if (string.Equals(enumName, "None", StringComparison.Ordinal)) continue;

                object enumValue = Enum.Parse(enumType, enumName);
                var typedEnumValue = Expression.Constant(enumValue, enumType);

                // fieldAccess |= enumValue
                Expression bitwiseOr = Expression.Assign(
                    fieldAccess,
                    Expression.Convert(
                        Expression.Or(
                            Expression.Convert(fieldAccess, underlying),
                            Expression.Convert(typedEnumValue, underlying)),
                        enumType));

                // fieldAccess &= ~enumValue
                Expression bitwiseAndNot = Expression.Assign(
                    fieldAccess,
                    Expression.Convert(
                        Expression.And(
                            Expression.Convert(fieldAccess, underlying),
                            Expression.Not(Expression.Convert(typedEnumValue, underlying))),
                        enumType));

                // if (element.TryGetProperty("EnumName", out propElement))
                //     if (propElement.GetBoolean()) field |= EnumValue;
                //     else                          field &= ~EnumValue;
                var tryGetProp = Expression.Call(elementParam, tryGetPropMethod,
                    Expression.Constant(enumName), propElementVar);

                var isTrue = Expression.Call(propElementVar, getBoolMethod);

                var inner = Expression.IfThenElse(isTrue, bitwiseOr, bitwiseAndNot);

                statements.Add(Expression.IfThen(tryGetProp, inner));
            }

            var block = Expression.Block(new[] { propElementVar }, statements);
            return Expression.Lambda<DebugStateSetter>(block, stateParam, elementParam).Compile();
        }

        private static DebugStateSetter CompilePrimitivePatcher(
            Type fieldType,
            Expression fieldAccess,
            ParameterExpression stateParam,
            ParameterExpression elementParam)
        {
            Expression readValue;

            if (fieldType == typeof(int))
            {
                readValue = Expression.Call(elementParam,
                    typeof(JsonElement).GetMethod(nameof(JsonElement.GetInt32), Type.EmptyTypes)!);
            }
            else if (fieldType == typeof(uint))
            {
                readValue = Expression.Call(elementParam,
                    typeof(JsonElement).GetMethod(nameof(JsonElement.GetUInt32), Type.EmptyTypes)!);
            }
            else if (fieldType == typeof(float))
            {
                readValue = Expression.Call(elementParam,
                    typeof(JsonElement).GetMethod(nameof(JsonElement.GetSingle), Type.EmptyTypes)!);
            }
            else if (fieldType == typeof(bool))
            {
                readValue = Expression.Call(elementParam,
                    typeof(JsonElement).GetMethod(nameof(JsonElement.GetBoolean), Type.EmptyTypes)!);
            }
            else if (fieldType == typeof(string))
            {
                readValue = Expression.Call(elementParam,
                    typeof(JsonElement).GetMethod(nameof(JsonElement.GetString), Type.EmptyTypes)!);
            }
            else
            {
                throw new NotSupportedException(
                    $"DebugStatePatchCompiler: unsupported field type '{fieldType}' on DebugState.");
            }

            var assign = Expression.Assign(fieldAccess, readValue);
            return Expression.Lambda<DebugStateSetter>(assign, stateParam, elementParam).Compile();
        }
    }
}
