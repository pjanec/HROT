using System;
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
        var descriptors = new System.Collections.Generic.List<EntityDescriptorUnion>
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
                // ⭐ CE-4073 — the neutral command is local ENU metres, the wire's frame: a copy, no conversion.
                WorldPos = new WorldPos
                {
                    Position = new Vec3d { X = cmd.X, Y = cmd.Y, Z = cmd.Z },
                }
            },
        };

        if (cmd.ForceId != 0)
        {
            descriptors.Add(new EntityDescriptorUnion
            {
                _d         = EDescriptorType.dtEntityInfo,
                EntityInfo = new EntityInfo { ForceIdentifier = MapForceId(cmd.ForceId) },
            });
        }

        return descriptors;
    }

    private static eForceIdentifier MapForceId(int forceId) => forceId switch
    {
        1 => eForceIdentifier.FORCE_FRIENDLY,
        2 => eForceIdentifier.FORCE_OPPOSING,
        3 => eForceIdentifier.FORCE_NEUTRAL,
        _ => eForceIdentifier.FORCE_UNKNOWN,
    };

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
        // Include affiliation in InitialAttributesJson so CreateEntityRequestSystem
        // processes it via the JSON-attribute path. The dtEntityInfo descriptor in
        // InitialDescriptors carries the same data for older SimHost processors, but
        // NedEntityCreationRequestSource only extracts TkbType/DisType from descriptors
        // and passes InitialAttributesJson directly to CreateEntityRequestSystem.
        string? attrsJson = null;
        if (cmd.ForceId != 0)
        {
            string forceStr = cmd.ForceId switch
            {
                1 => "FORCE_FRIENDLY",
                2 => "FORCE_OPPOSING",
                3 => "FORCE_NEUTRAL",
                _ => "FORCE_UNKNOWN",
            };
            attrsJson = "{\"Affiliation\":\"" + forceStr + "\"}";
        }

        return new CreateEntityRequest
        {
            RequestId             = cmd.RequestId,
            Owner                 = new NodeId { AppDomainId = 0, AppInstanceId = 0 },
            Flags                 = 0,
            InitialDescriptors    = BuildCreateEntityDescriptors(cmd),
            InitialAttributesJson = attrsJson,
        };
    }

    /// <summary>
    /// Translates a neutral <see cref="UpdateEntityDescriptorCommand"/> to a NED
    /// <see cref="UpdateEntityDescriptorRequest"/>.
    /// </summary>
    public static UpdateEntityDescriptorRequest ToUpdateDescriptorRequest(
        UpdateEntityDescriptorCommand cmd)
    {
        var payload = new EntityDescriptorUnion();

        if (!string.IsNullOrEmpty(cmd.DescriptorJson))
        {
            try
            {
                using var doc  = System.Text.Json.JsonDocument.Parse(cmd.DescriptorJson);
                var       root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "WorldPos")
                {
                    // ⭐ CE-4073 — local ENU metres, keys x / y / z (they were lat / lon / alt).
                    double x = root.TryGetProperty("x", out var xp) ? xp.GetDouble() : 0;
                    double y = root.TryGetProperty("y", out var yp) ? yp.GetDouble() : 0;
                    double z = root.TryGetProperty("z", out var zp) ? zp.GetDouble() : 0;

                    payload = new EntityDescriptorUnion
                    {
                        _d       = EDescriptorType.dtWorldPos,
                        WorldPos = new WorldPos
                        {
                            EntityId = cmd.EntityId,
                            // AQ107 B / D — a MOVE request: the position (local ENU metres, copied); every other field zero
                            // (the receiver, UpdateEntityDescriptorRequestSystem, keeps the entity's own rotation and ignores the rest).
                            Position = new Vec3d { X = x, Y = y, Z = z },
                        },
                    };
                }
            }
            catch
            {
                // Malformed JSON — send without payload (best-effort)
            }
        }

        return new UpdateEntityDescriptorRequest
        {
            RequestId      = Guid.NewGuid(),
            EntityId       = cmd.EntityId,
            DescriptorType = EDescriptorType.dtWorldPos,
            CurrentVersion = (int)cmd.BaseVersion,
            Payload        = payload,
        };
    }
}
