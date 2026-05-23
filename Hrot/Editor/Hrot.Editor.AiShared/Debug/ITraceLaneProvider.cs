namespace Hrot.Editor.AiShared.Debug;

/// <summary>Describes one swim lane in the Trace Timeline window.</summary>
public sealed record TraceLaneDescriptor(
    string Id,
    string DisplayName,
    TraceLevel SupportedLevels);

/// <summary>
/// Subsystem-provided swim-lane definitions for the TraceTimelineWindow.
/// One implementation per subsystem; selected by matching Kind to the active asset.
/// </summary>
public interface ITraceLaneProvider
{
    AssetKind Kind { get; }
    IReadOnlyList<TraceLaneDescriptor> Lanes { get; }
}
