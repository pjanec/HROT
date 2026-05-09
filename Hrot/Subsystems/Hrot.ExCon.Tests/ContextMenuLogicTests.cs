using Hrot.Core.Network;
using Hrot.Core.Mission;
using Hrot.ExCon.Logic;
using Fdp.Toolkit.DER;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Hrot.ExCon.Tests;


internal sealed class PushedContextActions
{
    public int MapGroupId { get; init; }
    public IReadOnlyList<int>? ForSelection { get; init; }
    public string MenuDefinitionJson { get; init; } = string.Empty;
}
internal sealed class CapturingEgressWriters : IExConEgressWriters
{
    public List<PushedContextActions> Written { get; } = new();
    public List<MapConfigDto> WrittenConfigs { get; } = new();
    public List<CreateEntityCommand> WrittenCreateCommands { get; } = new();
    public List<int> DeletedEntityIds { get; } = new();
    public List<MapCommandDto> WrittenMapCommands { get; } = new();

    public void WriteMapConfig(MapConfigDto config)        => WrittenConfigs.Add(config);
    public void WriteCreateEntity(CreateEntityCommand cmd) => WrittenCreateCommands.Add(cmd);
    public void WriteDeleteEntity(int entityId)            => DeletedEntityIds.Add(entityId);
    public void WriteMapCommand(MapCommandDto cmd)         => WrittenMapCommands.Add(cmd);
    public void PushContextActions(int mapGroupId, IReadOnlyList<int>? forSelection, string actionsJson)
        => Written.Add(new PushedContextActions { MapGroupId = mapGroupId, ForSelection = forSelection, MenuDefinitionJson = actionsJson });
    public void Dispose() { }
}
