using System;
using System.Collections.Generic;
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

        // ⭐ CE-4565 (X-10; CLAUDE.md HOT PATH) — the ring holds SLOTS reused for ever (allocated once), and a slot keeps the event and what names it;
        //   the summary TEXT is built when the history is READ (GetHistory: the Event Browser, the debug API), never per tick. 📐 Capture built a
        //   reflection + LINQ string for every event of every tick (and a DTO, and two iterators per stream). Now a tick costs nothing but the box of
        //   an unmanaged event (a managed one is kept as is).
        private sealed class Slot
        {
            public uint Frame;
            public string ProviderName = "";
            public Type? EventType;
            public bool IsManaged;
            public object? Event;
        }

        private readonly Slot[] _buffer = new Slot[Capacity];
        private int _head;   // index of the oldest entry (next write position)
        private int _count;  // how many entries are valid (0..Capacity)
        private readonly object _lock = new();

        public DiagnosticEventHistoryService()
        {
            for (int i = 0; i < Capacity; i++) _buffer[i] = new Slot();
        }

        /// <inheritdoc/>
        public void Capture(string providerName, FdpEventBus eventBus, uint currentFrame)
        {
            if (eventBus == null) return;

            var inspectors = eventBus.DebugInspectors();
            for (int s = 0; s < inspectors.Length; s++)
            {
                var inspector = inspectors[s];
                int n = inspector.Count;
                if (n == 0) continue;
                var type = inspector.EventType;
                bool isManaged = !type.IsValueType;
                for (int i = 0; i < n; i++)
                {
                    object evt = inspector.ReadItem(i);
                    lock (_lock)
                    {
                        var slot = _buffer[_head];
                        slot.Frame = currentFrame; slot.ProviderName = providerName; slot.EventType = type; slot.IsManaged = isManaged; slot.Event = evt;
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
                int start = (_head - _count + Capacity) % Capacity;   // the oldest entry
                int kept = 0;
                snapshot = new CapturedEventDto[_count];
                for (int i = 0; i < _count; i++)
                {
                    var slot = _buffer[(start + i) % Capacity];
                    if (providerFilter is { Count: > 0 } && !Contains(providerFilter, slot.ProviderName)) continue;
                    snapshot[kept++] = new CapturedEventDto(slot.Frame, slot.ProviderName, slot.EventType!.Name, slot.IsManaged, null!, slot.Event);
                }
                if (kept != snapshot.Length) Array.Resize(ref snapshot, kept);
            }
            return snapshot;   // ⭐ each DTO builds its summary on first read (CapturedEventDto.Summary)
        }

        private static bool Contains(IReadOnlyList<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++) if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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

        /// <inheritdoc/>
        public void RewindHistory(uint toFrame)
        {
            lock (_lock)
            {
                while (_count > 0)
                {
                    int newestIndex = (_head - 1 + Capacity) % Capacity;
                    if (_buffer[newestIndex].Frame >= toFrame)
                    {
                        _buffer[newestIndex].Event = null;
                        _head = newestIndex;
                        _count--;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        // the members a summary names, per type, found once (reflection runs on the first read of a type, never per tick)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, MemberInfo[]> SummaryMembers = new();

        /// <summary>The summary text of an event: its first three primitive / string / enum fields (or, with none, properties) as
        /// <c>Name:value</c>, else <c>ToString()</c>. Built on READ (<see cref="CapturedEventDto.Summary"/>).</summary>
        public static string SummaryOf(object? evt)
        {
            if (evt == null) return "null";
            var members = SummaryMembers.GetOrAdd(evt.GetType(), FindMembers);
            if (members.Length == 0) return evt.ToString() ?? "null";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < members.Length; i++)
            {
                if (i > 0) sb.Append("  ");
                sb.Append(members[i].Name).Append(':');
                try { sb.Append(FormatLeaf(members[i] is FieldInfo f ? f.GetValue(evt) : ((PropertyInfo)members[i]).GetValue(evt))); }
                catch { sb.Append("<err>"); }
            }
            return sb.ToString();
        }

        private static bool IsLeaf(Type t) => t.IsPrimitive || t == typeof(string) || t.IsEnum;

        private static MemberInfo[] FindMembers(Type type)
        {
            var found = new List<MemberInfo>(3);
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (IsLeaf(f.FieldType) && found.Count < 3) found.Add(f);
            if (found.Count > 0) return found.ToArray();
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.GetIndexParameters().Length == 0 && IsLeaf(p.PropertyType) && found.Count < 3) found.Add(p);
            return found.ToArray();
        }

        private static string FormatLeaf(object? v)
            => v switch
            {
                null   => "null",
                float  f => f.ToString("G4", System.Globalization.CultureInfo.InvariantCulture),
                double d => d.ToString("G4", System.Globalization.CultureInfo.InvariantCulture),
                _        => v.ToString() ?? "null"
            };
    }
}
