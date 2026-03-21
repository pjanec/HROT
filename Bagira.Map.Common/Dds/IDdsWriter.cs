namespace Bagira.Map.Common.Dds;

/// <summary>
/// Thin abstraction over a DDS writer used by services. Decoupling from the
/// concrete CycloneDDS.Runtime.DdsWriter&lt;T&gt; allows unit tests to inject a stub
/// without needing a live DDS participant.
/// </summary>
public interface IDdsWriter<T>
{
    void Write(T sample);

    /// <summary>
    /// Tombstones the DDS instance identified by <paramref name="key"/>.
    /// For Transient-Local topics, this removes the cached sample from
    /// late-joining subscribers.
    /// </summary>
    void DisposeInstance(T key);
}
