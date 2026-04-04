namespace Hrot.Editor;

/// <summary>
/// Tracks whether the HROT Editor is running with its internal FDP SimHost
/// or connected to an external HROT SimHost over DDS.
/// </summary>
public enum SimHostMode
{
    /// <summary>Local FDP SimHost logic packs are installed and active.</summary>
    Internal = 0,

    /// <summary>Local logic packs are ejected; ACL translator packs are active.</summary>
    External = 1,
}
