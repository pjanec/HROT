using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Fdp.Core.Diagnostics
{
    /// <summary>
    /// Thread-safe circular buffer implementation of <see cref="IDiagnosticEventHistoryService"/>.
    /// The buffer is capped at <see cref="Capacity"/> events; oldest entries are overwritten
    /// when the buffer is full.
    /// </summary>
    public sealed class DiagnosticEventHistoryService : IDiagnosticEventHistoryService
    {
        /// <summary>Maximum number of events retained in the circular buffer.</summary>
        public const int Capacity = 500;

        private readonly CapturedEventDto[] _buffer = new CapturedEventDto[Capacity];
        private int _head;   // index of the oldest entry (next write position)
        private int _count;  // how many entries are valid (0..Capacity)
        private readonly object _lock = new();

        /// <inheritdoc/>
        public void Capture(FdpEventBus eventBus, uint currentFrame)
        {
            if (eventBus == null) return;

            foreach (var inspector in eventBus.GetDebugInspectors())
            {
                if (inspector.Count == 0) continue;

                bool isManaged = !inspector.EventType.IsValueType;

                foreach (var evt in inspector.InspectReadBuffer())
                {
                    string typeName = inspector.EventType.Name;
                    string summary  = GetGenericEventSummary(evt, inspector.EventType);

                    var dto = new CapturedEventDto(currentFrame, typeName, isManaged, summary, evt);

                    lock (_lock)
                    {
                        _buffer[_head] = dto;
                        _head = (_head + 1) % Capacity;
                        if (_count < Capacity) _count++;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public CapturedEventDto[] GetHistory(IReadOnlyList<string>? providerFilter = null)
        {
            CapturedEventDto[] snapshot;

            lock (_lock)
            {
                if (_count == 0) return Array.Empty<CapturedEventDto>();

                snapshot = new CapturedEventDto[_count];

                // The oldest entry is at (_head - _count + Capacity) % Capacity.
                int start = (_head - _count + Capacity) % Capacity;
                for (int i = 0; i < _count; i++)
                    snapshot[i] = _buffer[(start + i) % Capacity];
            }

            // Apply optional prefix filter outside the lock (no stalling the writer).
            if (providerFilter != null && providerFilter.Count > 0)
            {
                snapshot = snapshot
                    .Where(e => providerFilter.Any(p => e.TypeName.StartsWith(p, StringComparison.Ordinal)))
                    .ToArray();
            }

            return snapshot;
        }

        /// <inheritdoc/>
        public void ClearHistory()
        {
            lock (_lock)
            {
                _head  = 0;
                _count = 0;
            }
        }

        // ── Headless summary helper (no ImGui dependency) ─────────────────────

        private static string GetGenericEventSummary(object evt, Type type)
        {
            if (evt == null) return "null";

            // Try primitive / string / enum fields first (common for struct events).
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType.IsPrimitive || f.FieldType == typeof(string) || f.FieldType.IsEnum)
                .Take(3)
                .Select(f => $"{f.Name}:{FormatLeaf(f.GetValue(evt))}")
                .ToList();

            if (fields.Count > 0)
                return string.Join("  ", fields);

            // Fall back to non-indexed public properties (managed events / records).
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 &&
                           (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType.IsEnum))
                .Take(3)
                .Select(p =>
                {
                    try   { return $"{p.Name}:{FormatLeaf(p.GetValue(evt))}"; }
                    catch { return $"{p.Name}:<err>"; }
                })
                .ToList();

            if (props.Count > 0)
                return string.Join("  ", props);

            return evt.ToString() ?? "null";
        }

        private static string FormatLeaf(object? v)
            => v switch
            {
                null   => "null",
                float  f => f.ToString("G4"),
                double d => d.ToString("G4"),
                _        => v.ToString() ?? "null"
            };
    }
}
