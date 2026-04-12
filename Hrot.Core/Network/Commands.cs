namespace Hrot.Core.Network;

/// <summary>Protocol-neutral create-entity command.</summary>
public sealed class CreateEntityCommand
{
    public long TkbType { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    public string? PropertiesJson { get; set; }
    public int ForceId { get; set; }
}

/// <summary>Protocol-neutral update-entity-descriptor command.</summary>
public sealed class UpdateEntityDescriptorCommand
{
    public int EntityId { get; set; }
    public string DescriptorJson { get; set; } = string.Empty;
    public long BaseVersion { get; set; }
}

/// <summary>Protocol-neutral mission-control command (wrapper for a mission plan or imperative).</summary>
public sealed class MissionControlCommand
{
    public int EntityId { get; set; }
    public Hrot.Core.Mission.eMissionCommandType CommandType { get; set; }
    public Hrot.Core.Mission.MissionPlan? Plan { get; set; }
    public Guid TaskId { get; set; }
    public long BaseVersion { get; set; }
}

/// <summary>Protocol-neutral map config DTO.</summary>
public sealed class MapConfigDto
{
    public string ConfigJson { get; set; } = string.Empty;
}

/// <summary>Protocol-neutral map command DTO.</summary>
public sealed class MapCommandDto
{
    public string CommandJson { get; set; } = string.Empty;
}
