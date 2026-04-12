namespace Hrot.Map.Common;

/// <summary>
/// Tags a composite translator pack with its direction of data flow.
/// <see cref="Ingress"/> packs register DDS reader subscriptions only;
/// <see cref="Egress"/> packs register DDS writer publications only.
/// </summary>
public enum PackRole
{
    /// <summary>Pack subscribes to incoming DDS topics (reader only).</summary>
    Ingress,
    /// <summary>Pack publishes to outgoing DDS topics (writer only).</summary>
    Egress,
}
