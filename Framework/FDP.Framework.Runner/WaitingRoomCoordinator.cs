using System.Diagnostics;
using CycloneDDS.Runtime;

namespace FDP.Framework.Runner
{
    /// <summary>
    /// Distributed startup synchronisation for the Runner Waiting Room protocol.
    ///
    /// <para>Protocol:
    /// <list type="number">
    ///   <item>Publish own status with <c>Ready = false</c>.</item>
    ///   <item>Poll the <c>SubsystemStatusAnnounce</c> DDS topic until all required peers are seen.</item>
    ///   <item>Publish own status with <c>Ready = true</c> once all peers are discovered.</item>
    /// </list>
    /// TransientLocal QoS ensures late-joining processes receive announcements
    /// that were published before they subscribed.
    /// </para>
    /// </summary>
    public sealed class WaitingRoomCoordinator : IDisposable
    {
        // ── Constants ─────────────────────────────────────────────────────────
        /// <summary>Default maximum wait time in milliseconds before giving up.</summary>
        public const int WaitingRoomTimeoutMs = 30_000;

        /// <summary>Poll interval in milliseconds between DDS reads.</summary>
        private const int PollIntervalMs = 100;

        // ── State ─────────────────────────────────────────────────────────────
        private readonly DdsParticipant _participant;
        private readonly DdsWriter<SubsystemStatusAnnounce> _writer;
        private readonly DdsReader<SubsystemStatusAnnounce> _reader;
        private readonly int _localNodeId;
        private readonly string _subsystemName;
        private readonly HashSet<string> _requiredPeers;
        private readonly int _timeoutMs;
        private bool _disposed;

        // ── Construction ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates a coordinator using the default 30-second timeout.
        /// </summary>
        public WaitingRoomCoordinator(
            DdsParticipant participant,
            int localNodeId,
            string subsystemName,
            HashSet<string> requiredPeers)
            : this(participant, localNodeId, subsystemName, requiredPeers, WaitingRoomTimeoutMs)
        {
        }

        /// <summary>
        /// Creates a coordinator with a custom timeout.  Use a small value in tests.
        /// </summary>
        public WaitingRoomCoordinator(
            DdsParticipant participant,
            int localNodeId,
            string subsystemName,
            HashSet<string> requiredPeers,
            int timeoutMs)
        {
            _participant   = participant;
            _localNodeId   = localNodeId;
            _subsystemName = subsystemName;
            _requiredPeers = new HashSet<string>(requiredPeers, StringComparer.OrdinalIgnoreCase);
            _timeoutMs     = timeoutMs;

            _writer = new DdsWriter<SubsystemStatusAnnounce>(participant, "SubsystemStatusAnnounce");
            _reader = new DdsReader<SubsystemStatusAnnounce>(participant, "SubsystemStatusAnnounce");
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Blocks until all required peers have announced themselves, then publishes
        /// own <c>Ready = true</c> status.
        /// </summary>
        /// <exception cref="TimeoutException">
        /// Thrown when the waiting room timeout expires before all peers are discovered.
        /// </exception>
        public void WaitForPeers()
        {
            // Announce self (not yet ready)
            PublishStatus(ready: false);

            var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stopwatch  = Stopwatch.StartNew();

            while (discovered.Count < _requiredPeers.Count)
            {
                if (stopwatch.ElapsedMilliseconds > _timeoutMs)
                {
                    var missing = string.Join(", ", _requiredPeers.Except(discovered, StringComparer.OrdinalIgnoreCase));
                    throw new TimeoutException(
                        $"Waiting room timeout after {_timeoutMs}ms. " +
                        $"Expected peers: {string.Join(", ", _requiredPeers)}. " +
                        $"Discovered: {string.Join(", ", discovered)}. " +
                        $"Missing: {missing}");
                }

                // Poll DDS for new announcements
                PollPeers(discovered);

                if (discovered.Count < _requiredPeers.Count)
                    Thread.Sleep(PollIntervalMs);
            }

            // All peers discovered – announce ready
            PublishStatus(ready: true);
        }

        /// <summary>
        /// Returns a snapshot of discovered peer info as reported via DDS.
        /// Skips own NodeId.
        /// </summary>
        public List<SubsystemPeerInfo> GetDiscoveredPeers()
        {
            var result = new List<SubsystemPeerInfo>();
            using var loan = _reader.Take();
            for (int i = 0; i < loan.Count; i++)
            {
                var info = loan.Infos[i];
                if (info.ValidData == 0 || info.InstanceState != DdsInstanceState.Alive)
                    continue;

                var peer = loan[i];
                if (peer.NodeId == _localNodeId) continue;

                result.Add(new SubsystemPeerInfo
                {
                    NodeId            = peer.NodeId,
                    SubsystemName     = peer.SubsystemName,
                    DomainId          = peer.DomainId,
                    Ready             = peer.Ready,
                    LastSeenTimestamp = peer.Timestamp
                });
            }
            return result;
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
            _reader.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void PollPeers(HashSet<string> discovered)
        {
            using var loan = _reader.Take();
            for (int i = 0; i < loan.Count; i++)
            {
                var info = loan.Infos[i];
                if (info.ValidData == 0 || info.InstanceState != DdsInstanceState.Alive)
                    continue;

                var peer = loan[i];
                if (peer.NodeId == _localNodeId) continue;

                var peerNameLower = peer.SubsystemName?.ToLowerInvariant() ?? string.Empty;
                if (_requiredPeers.Contains(peerNameLower))
                    discovered.Add(peerNameLower);
            }
        }

        private void PublishStatus(bool ready)
        {
            var status = new SubsystemStatusAnnounce
            {
                NodeId        = _localNodeId,
                SubsystemName = _subsystemName,
                DomainId      = (int)_participant.DomainId,
                Ready         = ready,
                Timestamp     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            _writer.Write(status);
        }
    }
}
