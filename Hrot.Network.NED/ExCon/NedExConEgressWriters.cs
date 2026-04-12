using CycloneDDS.Runtime;
using Hrot.Core.Network;
using Hrot.NED.Common;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.ExCon;

/// <summary>
/// NED wire transport implementation of <see cref="IExConEgressWriters"/>.
/// Wraps DDS writers for ExCon entity lifecycle and map commands.
/// </summary>
public sealed class NedExConEgressWriters : IExConEgressWriters
{
    // JSON schema version constant embedded in MapInteractionConfig publications.
    private const int MapConfigSchemaVersion = 1;

    private readonly DdsWriter<MapInteractionConfig>  _configWriter;
    private readonly DdsWriter<CreateEntityRequest>   _createEntityWriter;
    private readonly DdsWriter<MapCommandRequest>     _commandWriter;
    private readonly DdsWriter<DeleteEntityRequest>   _deleteEntityWriter;
    private readonly DdsWriter<ContextActionsUpdate>  _contextMenuWriter;
    private readonly int                              _mapGroupId;

    public NedExConEgressWriters(DdsParticipant participant, int mapGroupId = 0)
    {
        _mapGroupId          = mapGroupId;
        _configWriter        = new DdsWriter<MapInteractionConfig>(participant);
        _createEntityWriter  = new DdsWriter<CreateEntityRequest>(participant);
        _commandWriter       = new DdsWriter<MapCommandRequest>(participant);
        _deleteEntityWriter  = new DdsWriter<DeleteEntityRequest>(participant);
        _contextMenuWriter   = new DdsWriter<ContextActionsUpdate>(participant);
    }

    /// <inheritdoc/>
    public void WriteMapConfig(MapConfigDto config)
    {
        _configWriter.Write(new MapInteractionConfig
        {
            MapGroupId        = _mapGroupId,
            MapId             = 0,
            ActiveContextId   = config.ActiveContextId,
            JsonSchemaVersion = MapConfigSchemaVersion,
            ConfigurationJson = config.ConfigJson,
        });
    }

    /// <inheritdoc/>
    public void WriteDeleteEntity(int entityId)
    {
        _deleteEntityWriter.Write(new DeleteEntityRequest
        {
            RequestId = Guid.NewGuid(),
            EntityId  = entityId,
        });
    }

    /// <inheritdoc/>
    public void WriteCreateEntity(CreateEntityCommand cmd)
    {
        _createEntityWriter.Write(new CreateEntityRequest
        {
            RequestId          = cmd.RequestId,
            Owner              = new NodeId { AppDomainId = 0, AppInstanceId = 0 },
            Flags              = 0,
            InitialDescriptors = NedTranslationHelper.BuildCreateEntityDescriptors(cmd),
        });
    }

    /// <inheritdoc/>
    public void WriteMapCommand(MapCommandDto cmd)
    {
        if (!System.Enum.TryParse<CommandType>(cmd.CommandType, ignoreCase: true, out var cmdType))
            return;

        _commandWriter.Write(new MapCommandRequest
        {
            RequestId       = cmd.RequestId,
            MapId           = cmd.TargetMapId,
            Type            = cmdType,
            CommandArgsJson = cmd.CommandArgsJson,
        });
    }

    /// <inheritdoc/>
    public void PushContextActions(int mapGroupId, System.Collections.Generic.IReadOnlyList<int>? forSelection, string actionsJson)
    {
        var sel = forSelection == null
            ? new System.Collections.Generic.List<int>()
            : new System.Collections.Generic.List<int>(forSelection);
        _contextMenuWriter.Write(new ContextActionsUpdate
        {
            MapGroupId         = mapGroupId,
            ForSelection       = sel,
            MenuDefinitionJson = actionsJson,
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _configWriter.Dispose();
        _createEntityWriter.Dispose();
        _commandWriter.Dispose();
        _deleteEntityWriter.Dispose();
        _contextMenuWriter.Dispose();
    }
}
