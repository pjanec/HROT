using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Fbt.Compiler
{
    /// <summary>
    /// Scans assemblies for methods annotated with <see cref="BTreeActionAttribute"/> or
    /// <see cref="BTreeConditionAttribute"/> and produces a <see cref="BTreeSchema"/>.
    /// This is a standalone tool utility; it does not run during normal engine startup.
    /// </summary>
    /// <remarks>
    /// FieldOffset is reported as -1 for all scanned methods.  Real byte offsets are computed
    /// at compile time by the Fbt.SourceGen Roslyn source generator, which has access to full
    /// type-layout information via the Roslyn Semantic Model.
    /// </remarks>
    public static class BTreeSchemaExporter
    {
        /// <summary>
        /// Scans <paramref name="assemblies"/> for <see cref="BTreeActionAttribute"/> and
        /// <see cref="BTreeConditionAttribute"/> methods and returns a <see cref="BTreeSchema"/>.
        /// Assemblies that cannot be reflected (COM, dynamic, etc.) are silently skipped.
        /// </summary>
        public static BTreeSchema Export(IEnumerable<Assembly> assemblies)
        {
            var actions = new List<ActionDescriptor>();
            var conditions = new List<ConditionDescriptor>();

            foreach (var assembly in assemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        foreach (var method in type.GetMethods(
                            BindingFlags.Static | BindingFlags.Instance |
                            BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (method.IsDefined(typeof(BTreeActionAttribute), false))
                            {
                                actions.Add(BuildActionDescriptor(method));
                            }
                            else if (method.IsDefined(typeof(BTreeConditionAttribute), false))
                            {
                                conditions.Add(BuildConditionDescriptor(method));
                            }
                        }
                    }
                }
                catch
                {
                    // Skip non-reflectable assemblies (COM, dynamic, etc.)
                }
            }

            var dtoTypes = actions.Select(a => a.BlackboardDtoType)
                .Concat(conditions.Select(c => c.BlackboardDtoType))
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .ToArray();

            return new BTreeSchema(actions.ToArray(), conditions.ToArray(), dtoTypes);
        }

        /// <summary>
        /// Serialises <paramref name="schema"/> to a JSON file at <paramref name="outputPath"/>
        /// using <c>System.Text.Json</c>. Does not throw if Actions and Conditions are empty.
        /// </summary>
        public static void ExportToJson(BTreeSchema schema, string outputPath)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(schema, options);
            File.WriteAllText(outputPath, json);
        }

        private static ActionDescriptor BuildActionDescriptor(MethodInfo method)
        {
            return new ActionDescriptor(
                MethodName: method.Name,
                DeclaringType: method.DeclaringType?.FullName ?? string.Empty,
                BlackboardDtoType: GetFirstParamTypeName(method),
                FieldName: string.Empty,
                FieldOffset: -1);
        }

        private static ConditionDescriptor BuildConditionDescriptor(MethodInfo method)
        {
            return new ConditionDescriptor(
                MethodName: method.Name,
                DeclaringType: method.DeclaringType?.FullName ?? string.Empty,
                BlackboardDtoType: GetFirstParamTypeName(method),
                FieldName: string.Empty,
                FieldOffset: -1);
        }

        /// <summary>
        /// Returns the fully-qualified name of the first parameter type (stripping ref/out).
        /// For 3-parameter reusable delegates the first param is the projected TValue;
        /// for 4-parameter full-blackboard delegates it is the full TBlackboard type.
        /// </summary>
        private static string GetFirstParamTypeName(MethodInfo method)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
                return string.Empty;

            var firstParamType = parameters[0].ParameterType;
            if (firstParamType.IsByRef)
                firstParamType = firstParamType.GetElementType()!;

            return firstParamType.FullName ?? firstParamType.Name;
        }
    }
}
