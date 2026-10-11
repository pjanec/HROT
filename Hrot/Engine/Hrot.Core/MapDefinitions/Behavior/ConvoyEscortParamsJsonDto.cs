namespace Hrot.Map.Definitions.Behavior
{
    [BehaviorContract(BehaviorId, BehaviorCategory.MilitaryApc, Demo = true)]   // ⭐ CE-3160 (L4) — an example / demo tree, not the library
    public sealed class ConvoyEscortParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ConvoyEscort;
    }
}
