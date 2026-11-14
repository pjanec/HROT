namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// Top-level blittable three-int struct used by the Make/Break/SetMembers struct tests and coverage fixtures.
/// ⛔ HISTORY — declared in <c>MultiPinSetSharedTests</c> for the multi-pin SetShared rail; that rail went with
/// the node pair (<c>CE-440</c>, <c>Q76</c> §12.24) and the struct moved here. The name is historical.
/// </summary>
public struct MultiPinShared
{
    public int A;
    public int B;
    public int C;
}
