namespace Fdp.Toolkit.Orchestration.Handlers;

/// <summary>
/// Domain payload for the DECLARATIVE scenario save (CE-275 ③), carried on the
/// <see cref="NodeOpType.SerializeLocal"/> fan-out that <c>StorageOpType.SaveScenarioJson</c> triggers.
///
/// <para>Discriminates the scenario-JSON save from the <c>.fdp</c> checkpoint/archive recording that
/// shares the same <see cref="NodeOpType.SerializeLocal"/> op: <c>ReferenceArchiveHandler</c> acts only on
/// an <see cref="ArchiveHandlerPayload"/>, the scenario save handler only on this one, so both can be
/// registered on the same node without colliding.</para>
///
/// <para><see cref="ScenarioName"/> is the relative name / subfolder under the NAS scenarios root — never a
/// full filesystem path (the operator picks at most a subfolder of the standard scenarios folder).</para>
///
/// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4.
/// </summary>
public readonly record struct ScenarioSaveHandlerPayload(string ScenarioName);
