using System;
using CycloneDDS.Runtime;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
{
    // Wraps a CycloneDDS.Runtime.DdsWriter<T> so that gizmo production code
    // can receive it through the IDdsWriter<T> abstraction without depending
    // on CycloneDDS directly.
    /// <summary>
    /// ⭐ <c>CE-4366</c> (L-4, docs/DESIGN_Scale_Rig.md §5) — a writer that knows whether any reader is matched: a publisher may then skip
    /// building a sample nobody receives (the gizmo batch: a fresh byte[] of every primitive, every frame — 200 KB … 1 MB per frame).
    /// </summary>
    public interface IReaderAwareWriter
    {
        bool HasReaders { get; }
    }

    public sealed class DdsWriterGizmoAdapter<T> : IDdsWriter<T>, IReaderAwareWriter, IDisposable
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

        /// <summary>Whether a reader of this topic is matched now (the publication-matched status).</summary>
        public bool HasReaders => !_disposed && _writer.CurrentStatus.CurrentCount > 0;

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
