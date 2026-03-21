using System;
using CycloneDDS.Runtime;

namespace Bagira.Map.Common.Dds;

/// <summary>
/// Live DDS implementation of <see cref="IDdsWriter{T}"/>.
///
/// <para>Wraps a <see cref="DdsWriter{T}"/> and forwards <see cref="Write"/>
/// calls directly to it. Exceptions from the underlying writer are propagated
/// to the caller.</para>
///
/// <para>Implements <see cref="IDisposable"/>: dispose the adapter to release
/// the underlying DDS writer and its associated resources.</para>
/// </summary>
/// <typeparam name="T">DDS topic type (must have a default constructor).</typeparam>
public sealed class DdsWriterAdapter<T> : IDdsWriter<T>, IDisposable
    where T : new()
{
    private readonly DdsWriter<T> _writer;
    private bool _disposed;

    /// <summary>
    /// Constructs a live DDS writer for the specified topic.
    /// </summary>
    /// <param name="participant">
    /// Active DDS participant used to create the underlying DDS writer.
    /// The participant must remain alive for the lifetime of this adapter.
    /// </param>
    /// <param name="topicName">
    /// DDS topic name. Overrides any [DdsTopic] attribute on <typeparamref name="T"/>.
    /// </param>
    public DdsWriterAdapter(DdsParticipant participant, string topicName)
    {
        if (participant == null) throw new ArgumentNullException(nameof(participant));
        if (string.IsNullOrEmpty(topicName))
            throw new ArgumentException("Topic name must not be null or empty.", nameof(topicName));

        _writer = new DdsWriter<T>(participant, topicName);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">Thrown when the adapter has been disposed.</exception>
    public void Write(T sample)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DdsWriterAdapter<T>));
        _writer.Write(sample);
    }

    /// <inheritdoc/>
    public void DisposeInstance(T key)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DdsWriterAdapter<T>));
        _writer.DisposeInstance(key);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Dispose();
    }
}
