using Hrot.Game;

namespace Hrot.AI.Trees;

public static class NoComments_BT
{
    public static BTreeBuilder<NoComments_BT_Blackboard, BTreeContext> CreateBuilder() =>
        new BTreeBuilder<NoComments_BT_Blackboard, BTreeContext>()
            .Sequence(s => s
                .Condition(dto => dto.EnemySpotted, Actions.Detect,
                           visualId: new Guid("11111111-0001-0000-0000-000000000001"))
                .Action(Actions.Attack,
                        visualId: new Guid("22222222-0001-0000-0000-000000000001")),
                visualId: new Guid("33333333-0001-0000-0000-000000000001"));

    [BTreeDefinition("NoComments_BT", AssetId = "eeeeeeee-0000-0001-0000-000000000001")]
    public static BehaviorTreeBlob Build() => CreateBuilder().Compile("NoComments_BT");

    [BTreeLayout("eeeeeeee-0000-0001-0000-000000000001")]
    public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()
        .Node("11111111-0001-0000-0000-000000000001",
              position: new Vector2(100f, 200f))
        .Node("22222222-0001-0000-0000-000000000001",
              position: new Vector2(300f, 200f))
        .Node("33333333-0001-0000-0000-000000000001",
              position: new Vector2(200f, 50f))
        .Build();
}
