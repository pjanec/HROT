using System;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using StructEdit.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// Validates a field path via StructEdit at construction time and compiles
    /// an Expression-tree delegate for allocation-minimal field access at evaluation time.
    /// </summary>
    /// <remarks>
    /// Design deviation: the original design described using IEditBuffer.ReplaceInstance for
    /// the hot path, but that method does not exist on IEditBuffer. Instead, a compiled
    /// Expression-tree delegate is used, which achieves the same zero-reflection-per-call goal.
    /// StructEdit is still used for path validation at construction time.
    /// </remarks>
    public sealed class PropertyEvaluator : IPropertyEvaluator
    {
        private readonly Func<object, object?> _getter;

        /// <summary>
        /// Constructs a PropertyEvaluator for the given component type and property path.
        /// </summary>
        /// <param name="editService">Used to validate that the path exists at construction time.</param>
        /// <param name="componentType">The ECS component type.</param>
        /// <param name="propertyPath">
        /// Dot-separated field or property path, e.g. "X" or "Position.X".
        /// </param>
        /// <exception cref="ArgumentException">Thrown if the path is invalid.</exception>
        public PropertyEvaluator(
            IComponentEditService editService,
            Type componentType,
            string propertyPath)
        {
            if (editService == null) throw new ArgumentNullException(nameof(editService));
            if (componentType == null) throw new ArgumentNullException(nameof(componentType));
            if (string.IsNullOrEmpty(propertyPath)) throw new ArgumentException("Property path must not be empty.", nameof(propertyPath));

            // Validate the path using StructEdit (constructs a dummy instance to open a session).
            object? dummy;
            try { dummy = Activator.CreateInstance(componentType); }
            catch { dummy = null; }

            using (var session = editService.Open(dummy!, componentType, EditScope.WholeComponent))
            {
                ValidatePathInDocument(session.Document.Root, propertyPath.Split('.'), 0, propertyPath);
            }

            // Compile Expression-tree delegate for allocation-minimal field access.
            _getter = BuildGetter(componentType, propertyPath);
        }

        /// <inheritdoc/>
        public string GetValueAsString(object component)
        {
            object? value = _getter(component);
            if (value == null) return "null";
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        }

        // ── Path validation via StructEdit document tree ────────────────────

        private static void ValidatePathInDocument(
            EditNode node,
            string[] segments,
            int segmentIndex,
            string fullPath)
        {
            if (segmentIndex >= segments.Length) return;

            string segment = segments[segmentIndex];
            EditNode? found = null;

            foreach (var child in node.Children)
            {
                if (string.Equals(child.Name, segment, StringComparison.Ordinal))
                {
                    found = child;
                    break;
                }
            }

            if (found == null)
                throw new ArgumentException(
                    $"Property path '{fullPath}' is invalid: segment '{segment}' not found on {node.Name ?? "(root)"}.",
                    "propertyPath");

            ValidatePathInDocument(found, segments, segmentIndex + 1, fullPath);
        }

        // ── Expression-tree delegate compilation ────────────────────────────

        private static Func<object, object?> BuildGetter(Type componentType, string propertyPath)
        {
            // Param: object obj
            var param = Expression.Parameter(typeof(object), "obj");

            // Convert to the concrete component type (unbox for structs, cast for classes)
            Expression access = Expression.Convert(param, componentType);

            // Navigate each path segment
            string[] segments = propertyPath.Split('.');
            foreach (string segment in segments)
            {
                // Try field first, then property
                MemberInfo? member =
                    (MemberInfo?)componentType.GetField(segment,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                    componentType.GetProperty(segment,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (member == null)
                    throw new ArgumentException(
                        $"Property path '{propertyPath}' is invalid: member '{segment}' not found on {componentType.Name}.");

                access = member is FieldInfo fi
                    ? Expression.Field(access, fi)
                    : Expression.Property(access, (PropertyInfo)member);

                // Update componentType for next segment (to navigate nested types)
                componentType = member is FieldInfo fi2 ? fi2.FieldType : ((PropertyInfo)member).PropertyType;
            }

            // Box the result as object (required for returning from Func<object, object?>)
            Expression result = Expression.Convert(access, typeof(object));

            return Expression.Lambda<Func<object, object?>>(result, param).Compile();
        }
    }
}
