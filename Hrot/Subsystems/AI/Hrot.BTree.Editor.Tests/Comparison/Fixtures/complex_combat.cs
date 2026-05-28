// HROT_EDITOR_GENERATED - managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: aaaaaaaa-0000-0002-0000-000000000001

using Hrot.Game.Combat;

namespace Hrot.AI.Trees;

public static class ComplexCombat_BT
{
    public static BTreeBuilder<CombatBlackboard, BTreeContext> CreateBuilder() =>
        new BTreeBuilder<CombatBlackboard, BTreeContext>()
            .Selector(s => s
                .Sequence(seq => seq
                    .Condition(dto => dto.HasTarget, CombatActions.FindTarget,
                               visualId: new Guid("bbbbbbbb-0000-0002-0000-000000000001"))
                    .Action(CombatActions.Attack,
                            visualId: new Guid("cccccccc-0000-0002-0000-000000000001")),
                    visualId: new Guid("dddddddd-0000-0002-0000-000000000001"))
                .Subtree("eeeeeeee-0000-0002-0000-000000000001",
                         visualId: new Guid("ffffffff-0000-0002-0000-000000000001")),
                visualId: new Guid("11111111-0000-0002-0000-000000000001"));

    [BTreeDefinition("ComplexCombat_BT", AssetId = "aaaaaaaa-0000-0002-0000-000000000001")]
    public static BehaviorTreeBlob Build() => CreateBuilder().Compile("ComplexCombat_BT");

    [BTreeLayout("aaaaaaaa-0000-0002-0000-000000000001")]
    public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()
        .Node("bbbbbbbb-0000-0002-0000-000000000001",
              position: new Vector2(100f, 300f),
              comment: "find nearest enemy")
        .Node("cccccccc-0000-0002-0000-000000000001",
              position: new Vector2(300f, 300f),
              comment: "execute attack sequence")
        .Node("dddddddd-0000-0002-0000-000000000001",
              position: new Vector2(200f, 150f))
        .Node("ffffffff-0000-0002-0000-000000000001",
              position: new Vector2(500f, 150f),
              comment: "delegate to retreat subtree")
        .SubtreeSyncField("ffffffff-0000-0002-0000-000000000001", "AmmoRemaining", masterVar: "TotalAmmo", syncIn: true, syncOut: false)
        .SubtreeSyncField("ffffffff-0000-0002-0000-000000000001", "RetreatStatus", masterVar: "LastStatus", syncIn: false, syncOut: true)
        .Node("11111111-0000-0002-0000-000000000001",
              position: new Vector2(300f, 0f))
        .Build();
}
