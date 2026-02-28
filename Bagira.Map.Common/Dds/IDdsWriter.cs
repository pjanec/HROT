namespace Bagira.Map.Common.Dds;

/// <summary>
/// Thin abstraction over a DDS writer used by services. Decoupling from the
/// concrete CycloneDDS.Runtime.DdsWriter&lt;T&gt; allows unit tests to inject a stub
/// without needing a live DDS participant.
/// </summary>
public interface IDdsWriter<T>
{
    void Write(T sample);
}
