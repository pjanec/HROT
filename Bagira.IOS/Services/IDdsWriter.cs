namespace Bagira.IOS.Services;

/// <summary>
/// Thin abstraction over a DDS writer used by services.  Decoupling from the
/// concrete <c>CycloneDDS.Runtime.DdsWriter&lt;T&gt;</c> allows unit tests to
/// inject a stub without needing a live DDS participant.
/// </summary>
public interface IDdsWriter<T>
{
    void Write(T sample);
}
