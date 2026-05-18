using System;
using System.Collections.Generic;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace GizmoMap.Network
{
    // Stateful transport adapter that publishes interned strings as keyed DDS instances.
    // Uses a local hash set to delta-publish only newly observed entries.
    public sealed class DdsStringInternPublisher
    {
        private readonly IDdsWriter<StringInternEntry> _writer;
        private readonly byte _nodeId;
        private readonly HashSet<uint> _publishedHashes = new();

        public DdsStringInternPublisher(IDdsWriter<StringInternEntry> writer, byte nodeId)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _nodeId = nodeId;
        }

        // Publishes newly discovered intern entries from the given map.
        public void Publish(StringInternMap internMap)
        {
            foreach (var kvp in internMap.Entries)
            {
                if (_publishedHashes.Add(kvp.Key))
                {
                    _writer.Write(new StringInternEntry
                    {
                        NodeId = _nodeId,
                        Hash = kvp.Key,
                        Text = kvp.Value,
                    });
                }
            }
        }
    }
}
