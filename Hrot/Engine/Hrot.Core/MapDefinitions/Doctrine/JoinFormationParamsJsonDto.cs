namespace Hrot.Map.Definitions.Doctrine
{
    /// <summary>
    /// Parameter DTO for the <c>JoinFormation</c> behavior.
    /// Currently parameterless; the contract exists to anchor the doctrine ID and category.
    /// </summary>
    [DoctrineContract(DoctrineIds.JoinFormation_BT, BehaviorId, DoctrineCategory.Infantry)]
    public sealed class JoinFormationParamsJsonDto
    {
        public const string BehaviorId = "JoinFormation";
    }
}
