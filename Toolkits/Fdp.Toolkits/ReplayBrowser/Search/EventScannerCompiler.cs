using System;
using System.Collections.Generic;
using System.Reflection;
using Fdp.Core;
using StructEdit.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// Compiles TransientEventPredicateDto into EventScannerDelegate instances.
    /// Three scanner branches: pure-occurrence, unmanaged-value, and managed-value.
    /// </summary>
    public sealed class EventScannerCompiler : IEventScannerCompiler
    {
        private readonly IComponentEditService _editService;

        // Cached generic MethodInfo instances for building scanner delegates.
        private static readonly MethodInfo _buildUnmanagedScannerMethod =
            typeof(EventScannerCompiler).GetMethod(
                nameof(BuildUnmanagedValueScanner),
                BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly MethodInfo _buildManagedScannerMethod =
            typeof(EventScannerCompiler).GetMethod(
                nameof(BuildManagedValueScanner),
                BindingFlags.NonPublic | BindingFlags.Static)!;

        public EventScannerCompiler(IComponentEditService editService)
        {
            _editService = editService ?? throw new ArgumentNullException(nameof(editService));
        }

        /// <inheritdoc/>
        public EventScannerDelegate CompileScanner(TransientEventPredicateDto predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            Type eventType = predicate.EventType
                ?? throw new ArgumentException("EventType must not be null.", nameof(predicate));

            // Branch 1: pure occurrence (no value filter).
            if (predicate.AnyOccurrence || string.IsNullOrEmpty(predicate.PropertyPath))
            {
                return BuildOccurrenceScanner(eventType);
            }

            // Build the value-filter scanner.
            string path = predicate.PropertyPath;
            SearchOperator op = predicate.Operator;
            string targetValue = predicate.TargetValue ?? string.Empty;

            var evaluator = new PropertyEvaluator(_editService, eventType, path);

            // Branch 2: unmanaged value scan (bus.Read<T>()).
            if (eventType.IsValueType)
            {
                var factoryMethod = _buildUnmanagedScannerMethod.MakeGenericMethod(eventType);
                return (EventScannerDelegate)factoryMethod.Invoke(
                    null, new object[] { evaluator, op, targetValue })!;
            }

            // Branch 3: managed value scan (bus.ReadManaged<T>()).
            {
                var factoryMethod = _buildManagedScannerMethod.MakeGenericMethod(eventType);
                return (EventScannerDelegate)factoryMethod.Invoke(
                    null, new object[] { evaluator, op, targetValue })!;
            }
        }

        // ── Branch 1: pure occurrence ────────────────────────────────────────

        private static EventScannerDelegate BuildOccurrenceScanner(Type eventType)
        {
            string typeName = eventType.Name;
            return (bus, frame, ticks, results) =>
            {
                if (bus.HasEvent(eventType))
                    results.Add(new SearchResultDto(frame, ticks, Entity.Null, typeName + " Occurred"));
            };
        }

        // ── Branch 2: unmanaged value scanner ───────────────────────────────

#pragma warning disable IDE0051 // Used via reflection
        private static EventScannerDelegate BuildUnmanagedValueScanner<T>(
            IPropertyEvaluator evaluator,
            SearchOperator op,
            string targetValue)
            where T : unmanaged
        {
            Func<string, bool> match = CompileStringMatch(op, targetValue);

            return (bus, frame, ticks, results) =>
            {
                ReadOnlySpan<T> events = bus.Read<T>();
                for (int i = 0; i < events.Length; i++)
                {
                    object boxed = events[i]; // boxes the value once per event
                    string val = evaluator.GetValueAsString(boxed);
                    if (match(val))
                    {
                        Entity entity = TryExtractEntity(val);
                        results.Add(new SearchResultDto(frame, ticks, entity, typeof(T).Name + " " + val));
                    }
                }
            };
        }
#pragma warning restore IDE0051

        // ── Branch 3: managed value scanner ─────────────────────────────────

#pragma warning disable IDE0051 // Used via reflection
        private static EventScannerDelegate BuildManagedValueScanner<T>(
            IPropertyEvaluator evaluator,
            SearchOperator op,
            string targetValue)
        {
            Func<string, bool> match = CompileStringMatch(op, targetValue);
            string typeName = typeof(T).Name;

            return (bus, frame, ticks, results) =>
            {
                IReadOnlyList<T> events = bus.ReadManaged<T>();
                for (int i = 0; i < events.Count; i++)
                {
                    object? boxed = events[i];
                    if (boxed == null) continue;
                    string val = evaluator.GetValueAsString(boxed);
                    if (match(val))
                    {
                        Entity entity = TryExtractEntity(val);
                        results.Add(new SearchResultDto(frame, ticks, entity, typeName + " " + val));
                    }
                }
            };
        }
#pragma warning restore IDE0051

        // ── Operator match helper ────────────────────────────────────────────

        private static Func<string, bool> CompileStringMatch(SearchOperator op, string target)
        {
            return op switch
            {
                SearchOperator.Equals    => v => string.Equals(v, target, StringComparison.OrdinalIgnoreCase),
                SearchOperator.Contains  => v => v.Contains(target, StringComparison.OrdinalIgnoreCase),
                SearchOperator.StartsWith => v => v.StartsWith(target, StringComparison.OrdinalIgnoreCase),
                SearchOperator.GreaterThan => v =>
                    double.TryParse(v, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double dv) &&
                    double.TryParse(target, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double dt) &&
                    dv > dt,
                SearchOperator.LessThan => v =>
                    double.TryParse(v, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double dv) &&
                    double.TryParse(target, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double dt) &&
                    dv < dt,
                _ => _ => true
            };
        }

        // ── Entity deep-link extraction ──────────────────────────────────────

        /// <summary>
        /// Attempts to parse an entity handle from a string in the format "[index, vN]".
        /// Returns Entity.Null if the format does not match.
        /// </summary>
        internal static Entity TryExtractEntity(string value)
        {
            // Expected format: "[42, v3]" or "[42,v3]"
            if (string.IsNullOrEmpty(value) || value[0] != '[')
                return Entity.Null;

            int closeBracket = value.IndexOf(']');
            if (closeBracket < 0) return Entity.Null;

            string inner = value.Substring(1, closeBracket - 1); // "42, v3"
            int commaIdx = inner.IndexOf(',');
            if (commaIdx < 0) return Entity.Null;

            string indexStr = inner.Substring(0, commaIdx).Trim();
            string genStr   = inner.Substring(commaIdx + 1).Trim(); // "v3"

            if (!int.TryParse(indexStr, out int index)) return Entity.Null;

            // Strip 'v' prefix from generation
            if (genStr.Length < 2 || (genStr[0] != 'v' && genStr[0] != 'V'))
                return Entity.Null;

            if (!int.TryParse(genStr.Substring(1), out int generation)) return Entity.Null;

            return new Entity(index, (ushort)generation);
        }
    }
}
