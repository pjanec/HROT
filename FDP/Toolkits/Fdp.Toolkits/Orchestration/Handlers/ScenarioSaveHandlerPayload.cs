namespace Fdp.Toolkit.Orchestration.Handlers;

/// <summary>
/// Domain payload for the DECLARATIVE scenario save (CE-275 ③), carried on the
/// <see cref="NodeOpType.SerializeLocal"/> fan-out that <c>StorageOpType.SaveScenario</c> triggers.
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
public readonly record struct ScenarioSaveHandlerPayload(
    string ScenarioName,
    // ⭐ CE-4210 MA-9 — Save selection as macro: the entity GUIDs to write (null = everything, as today) and the macro header fields
    //   (the object ScenarioHeaderJson.Write produces; null = from the resident, as today). Both optional: a null / empty value keeps every existing save as it was.
    System.Collections.Generic.IReadOnlyList<System.Guid>? OnlyGuids = null,
    string? HeaderJson = null);
