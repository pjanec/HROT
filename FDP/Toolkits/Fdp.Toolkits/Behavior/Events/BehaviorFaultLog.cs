using System;
using System.Collections.Generic;
using Fdp.Core.Logging;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// ⭐ <b><c>CE-484</c> — the operator sees a behaviour fault: the "Behaviour faults" tab of the Message Log.</b>
    /// 📄 <c>docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md</c> §4c W5.
    ///
    /// <para>Every fault is an <see cref="LogSeverity.Error"/> row, so the tab turns red until it is looked at. Two
    /// producers feed the ONE <see cref="Shared"/> instance: <see cref="BehaviorFault.Raise"/> on the node that raised it
    /// (this is what covers a host without DDS, e.g. the offline editor) and the <c>BehaviorFault</c> DDS ingress for faults
    /// raised on other nodes. <see cref="Report"/> de-duplicates on <see cref="FaultKey"/>, so a process hosting several
    /// nodes (<c>--mode all</c>) — or a node that receives its own sample back — shows each fault once.</para>
    ///
    /// <para>A static shared source, on the <c>AiBehaviorLogTarget.SharedInstance</c> precedent: hosts register it with
    /// their <c>MessageLogRegistry</c> (idempotent).</para>
    /// </summary>
    public sealed class BehaviorFaultLog : IMessageLogSource
    {
        /// <summary>One fault, wherever it was seen: the entity (its network id, or <c>-(index+1)</c> on a node without
        /// one), the behaviour run and the behaviour.</summary>
        public readonly record struct FaultKey(long Entity, uint InstanceId, int BehaviorHash);

        /// <summary>The process-wide source the hosts register.</summary>
        public static BehaviorFaultLog Shared { get; } = new();

        /// <summary>How many keys are remembered for de-duplication; the oldest is forgotten first.</summary>
        public const int RememberedKeys = 4096;

        private readonly List<MessageLogEntry> _messages = new();
        private readonly HashSet<FaultKey> _seen = new();
        private readonly Queue<FaultKey> _order = new();
        private readonly object _lock = new();

        public string SourceId    => "behavior_faults";
        public string DisplayName => "Behaviour Faults";

        public event Action<MessageLogEntry>? OnMessageAdded;

        /// <summary>The entity part of a <see cref="FaultKey"/> for a node that has no network id for it.</summary>
        public static long LocalEntityKey(int entityIndex) => -(entityIndex + 1L);

        /// <summary>
        /// Add a fault row unless <paramref name="key"/> was already reported. Returns true when a row was added.
        /// </summary>
        /// <param name="key">The de-duplication key.</param>
        /// <param name="behavior">The behaviour's name, or its hash when no name is known.</param>
        /// <param name="entityLabel">How to name the entity in the row (e.g. <c>"entity 1006"</c>).</param>
        /// <param name="code">The fault code (<see cref="BehaviorFaultCode"/> or a custom one).</param>
        /// <param name="message">The reason the behaviour gave.</param>
        public bool Report(FaultKey key, string behavior, string entityLabel, int code, string message)
        {
            string codeName = code >= 0 && code <= ushort.MaxValue && Enum.IsDefined(typeof(BehaviorFaultCode), (ushort)code)
                ? ((BehaviorFaultCode)(ushort)code).ToString()
                : code.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string text = $"{entityLabel}: behaviour '{behavior}' FAULTED ({codeName}): {message}";
            MessageLogEntry entry;
            lock (_lock)
            {
                if (!_seen.Add(key)) return false;
                _order.Enqueue(key);
                if (_order.Count > RememberedKeys) _seen.Remove(_order.Dequeue());
                entry = new MessageLogEntry(DateTime.Now, LogSeverity.Error, "Behaviour", text, LogSyntaxHighlighter.Parse(text));
                _messages.Add(entry);
            }
            OnMessageAdded?.Invoke(entry);
            return true;
        }

        public IReadOnlyList<MessageLogEntry> GetMessages()
        {
            lock (_lock) return _messages.ToArray();
        }

        /// <summary>Clears the rows. ⚠ The de-duplication memory is kept, so a fault already shown is not re-added by a late
        /// duplicate.</summary>
        public void Clear()
        {
            lock (_lock) _messages.Clear();
        }
    }
}
