using System;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace GizmoMap.Network
{
    // Stateless transport adapter that reads StringInternEntry samples from DDS
    // and applies them into a target buffer's StringInternMap.
    public sealed class DdsStringInternSubscriber
    {
        private readonly IDdsReader<StringInternEntry> _reader;

        public DdsStringInternSubscriber(IDdsReader<StringInternEntry> reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        // Drains all pending StringInternEntry samples and applies them to target.
        public void PollAndApply(GizmoPrimitiveBuffer target)
        {
            while (_reader.TryRead(out var entry))
            {
                target.InternMap.Intern(entry.Hash, entry.Text);
            }
        }
    }
}
