namespace Bagira.IG.Abstractions;

/// <summary>
/// Thin abstraction over a DDS DataWriter, allowing production code to publish
/// typed DDS samples while remaining testable without a live DDS participant.
/// </summary>
/// <typeparam name="T">The DDS topic struct to publish.</typeparam>
public interface IDdsWriter<T>
{
    /// <summary>Publishes <paramref name="sample"/> to the DDS topic.</summary>
    void Write(T sample);
}
