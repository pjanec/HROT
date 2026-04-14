using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace FDP.Toolkit.Scenario
{
    /// <summary>
    /// Empty tag component that instructs the scenario serializer to skip the entire
    /// entity bearing it.  Entities with this component do not appear in
    /// <c>dom["Entities"]</c> and are not recreated during load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Marked <c>[DataPolicy(DataPolicy.NoSave)]</c> so that
    /// <see cref="EntityRepository.GetSaveableMask()"/> never sets the bit for this
    /// component; the serializer therefore never tries to serialize <em>the tag
    /// itself</em> — it is used only as an entity-level filter.
    /// </para>
    /// <para>
    /// Usage: <c>repo.AddComponent(entity, new ScenarioIgnoreTag())</c> to exclude
    /// an entity from all scenario saves.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Size = 1)]
    [ComponentId(ScenarioComponentIds.ScenarioIgnoreTag)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct ScenarioIgnoreTag { }
}
