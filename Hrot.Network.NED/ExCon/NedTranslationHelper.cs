using Hrot.Core.Network;
using Hrot.NED.Common;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.ExCon;

/// <summary>
/// Static helpers for translating between neutral Core DTOs and NED wire types.
/// </summary>
internal static class NedTranslationHelper
{
    /// <summary>
    /// Builds the minimal initial descriptor list for a create-entity request.
    /// </summary>
    public static System.Collections.Generic.List<EntityDescriptorUnion> BuildCreateEntityDescriptors(
        CreateEntityCommand cmd)
    {
        return new System.Collections.Generic.List<EntityDescriptorUnion>
        {
            new EntityDescriptorUnion
            {
                _d           = EDescriptorType.dtEntityMaster,
                EntityMaster = new EntityMaster
                {
                    EntityId = -1,
                    TkbType  = cmd.TkbType,
                }
            },
            new EntityDescriptorUnion
            {
                _d       = EDescriptorType.dtWorldPos,
                WorldPos = new WorldPos
                {
                    Pos = new GeoPoint
                    {
                        Latitude  = cmd.Latitude,
                        Longitude = cmd.Longitude,
                        Altitude  = cmd.Altitude,
                    }
                }
            },
        };
    }

    /// <summary>
    /// Translates a neutral <see cref="MissionControlCommand"/> to a NED
    /// <see cref="MissionControlRequest"/>.
    /// </summary>
    public static MissionControlRequest ToMissionControlRequest(MissionControlCommand cmd)
    {
        var payload = new MissionCommandUnion
        {
            _d = (eMissionCommandType)(int)cmd.CommandType,
        };

        if (cmd.CommandType == Hrot.Core.Mission.eMissionCommandType.CMD_REPLACE_MISSION && cmd.Plan != null)
        {
            payload.FullMissionData = NedMissionHelper.ToNed(cmd.Plan);
        }
        else if (cmd.CommandType != Hrot.Core.Mission.eMissionCommandType.CMD_ABORT_ALL)
        {
            payload.TargetTaskId = cmd.TaskId;
        }

        return new MissionControlRequest
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = cmd.EntityId,
            BaseVersion    = cmd.BaseVersion,
            Payload        = payload,
        };
    }

    /// <summary>
    /// Translates a neutral <see cref="CreateEntityCommand"/> to a NED
    /// <see cref="CreateEntityRequest"/>.
    /// </summary>
    public static CreateEntityRequest ToCreateEntityRequest(CreateEntityCommand cmd)
    {
        return new CreateEntityRequest
        {
            RequestId          = cmd.RequestId,
            Owner              = new NodeId { AppDomainId = 0, AppInstanceId = 0 },
            Flags              = 0,
            InitialDescriptors = BuildCreateEntityDescriptors(cmd),
        };
    }

    /// <summary>
    /// Translates a neutral <see cref="UpdateEntityDescriptorCommand"/> to a NED
    /// <see cref="UpdateEntityDescriptorRequest"/>.
    /// </summary>
    public static UpdateEntityDescriptorRequest ToUpdateDescriptorRequest(
        UpdateEntityDescriptorCommand cmd)
    {
        return new UpdateEntityDescriptorRequest
        {
            RequestId      = Guid.NewGuid(),
            EntityId       = cmd.EntityId,
            DescriptorType = Hrot.NED.Descriptors.EDescriptorType.dtEntityMaster,
            CurrentVersion = (int)cmd.BaseVersion,
        };
    }
}
