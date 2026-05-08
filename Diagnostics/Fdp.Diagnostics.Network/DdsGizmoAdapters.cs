using System;
using CycloneDDS.Runtime;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
{
    // Wraps a CycloneDDS.Runtime.DdsWriter<T> so that gizmo production code
    // can receive it through the IDdsWriter<T> abstraction without depending
    // on CycloneDDS directly.
    public sealed class DdsWriterGizmoAdapter<T> : IDdsWriter<T>, IDisposable
        where T : new()
    {
        private readonly DdsWriter<T> _writer;
        private bool _disposed;

        public DdsWriterGizmoAdapter(DdsParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            _writer = new DdsWriter<T>(participant);
        }

        public void Write(T sample)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DdsWriterGizmoAdapter<T>));
            _writer.Write(sample);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }

    // Wraps a CycloneDDS.Runtime.DdsReader<T> so that gizmo production code
    // can receive it through the IDdsReader<T> abstraction without depending
    // on CycloneDDS directly.
    public sealed class DdsReaderGizmoAdapter<T> : IDdsReader<T>, IDisposable
        where T : new()
    {
        private readonly DdsReader<T> _reader;
        private bool _disposed;

        public DdsReaderGizmoAdapter(DdsParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            _reader = new DdsReader<T>(participant);
        }

        public bool TryRead(out T sample)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DdsReaderGizmoAdapter<T>));
            using var loan = _reader.Take(maxSamples: 1);
            if (loan.Count > 0)
            {
                sample = loan[0];
                return true;
            }
            sample = default!;
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _reader.Dispose();
        }
    }
}
