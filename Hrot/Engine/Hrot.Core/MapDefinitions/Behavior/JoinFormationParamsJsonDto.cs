namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// Parameter DTO for the <c>JoinFormation</c> behavior.
    /// Currently parameterless; the contract exists to anchor the behavior ID and category.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.Infantry)]
    public sealed class JoinFormationParamsJsonDto
    {
        public const string BehaviorId = "JoinFormation";
    }
}
