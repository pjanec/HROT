// HROT_EDITOR_GENERATED - managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: aaaaaaaa-0000-0001-0000-000000000001

using Hrot.Game;

namespace Hrot.AI.Trees;

public static class SimpleGuard_BT
{
    public static BTreeBuilder<SimpleGuard_BT_Blackboard, BTreeContext> CreateBuilder() =>
        new BTreeBuilder<SimpleGuard_BT_Blackboard, BTreeContext>()
            .Sequence(s => s
                .Condition(dto => dto.EnemySpotted, GuardActions.DetectEnemy,
                           visualId: new Guid("bbbbbbbb-0000-0001-0000-000000000001"))
                .Action(GuardActions.SoundAlarm,
                        visualId: new Guid("cccccccc-0000-0001-0000-000000000001")),
                visualId: new Guid("dddddddd-0000-0001-0000-000000000001"));

    [BTreeDefinition("SimpleGuard_BT", AssetId = "aaaaaaaa-0000-0001-0000-000000000001")]
    public static BehaviorTreeBlob Build() => CreateBuilder().Compile("SimpleGuard_BT");

    [BTreeLayout("aaaaaaaa-0000-0001-0000-000000000001")]
    public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()
        .Node("bbbbbbbb-0000-0001-0000-000000000001",
              position: new Vector2(100f, 200f),
              comment: "check if enemy is visible")
        .Node("cccccccc-0000-0001-0000-000000000001",
              position: new Vector2(300f, 200f),
              comment: "alert the base")
        .Node("dddddddd-0000-0001-0000-000000000001",
              position: new Vector2(200f, 50f))
        .Build();
}
