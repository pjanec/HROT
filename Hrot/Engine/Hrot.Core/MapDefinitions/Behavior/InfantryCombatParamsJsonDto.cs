namespace Hrot.Map.Definitions.Behavior
{
    [BehaviorContract(BehaviorId, BehaviorCategory.Infantry, Demo = true)]   // ⭐ CE-3160 (L4) — an example / demo tree, not the library
    public sealed class InfantryCombatParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.InfantryCombat;
    }
}
