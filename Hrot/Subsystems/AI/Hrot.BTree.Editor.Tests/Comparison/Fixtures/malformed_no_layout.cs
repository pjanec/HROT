// AssetId: aaaaaaaa-0000-0003-0000-000000000001

namespace Hrot.AI.Trees;

public static class NoLayout_BT
{
    public static BTreeBuilder<BB, BTreeContext> CreateBuilder() =>
        new BTreeBuilder<BB, BTreeContext>()
            .Action(SomeActions.DoThing,
                    visualId: new Guid("bbbbbbbb-0000-0003-0000-000000000001"));

    [BTreeDefinition("NoLayout_BT", AssetId = "aaaaaaaa-0000-0003-0000-000000000001")]
    public static BehaviorTreeBlob Build() => CreateBuilder().Compile("NoLayout_BT");
}
