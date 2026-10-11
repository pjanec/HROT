namespace Hrot.Map.Definitions.Behavior
{
    [BehaviorContract(BehaviorId, BehaviorCategory.Insurgent, Demo = true)]   // ⭐ CE-3160 (L4) — an example / demo tree, not the library
    public sealed class AmbushParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Ambush;
    }
}
