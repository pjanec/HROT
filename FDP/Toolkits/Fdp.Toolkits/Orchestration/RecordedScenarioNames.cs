using System;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐ <c>CE-4091</c> (AQ108 D7, D22) — the NAME under which a recording's scenario copy is opened for editing:
    /// <c>opened/recorded/&lt;exerciseId&gt;</c> (<c>CE-3340</c> ①: was <c>Recipes/Recorded/&lt;id&gt;</c>, inside the folder "New" lists).
    /// ⭐ A scenario load is a NAME the master resolves against the NAS through <see cref="ContentLayout.ResolveScenarioDirectory"/>; a name
    /// under <see cref="ContentLayout.OpenedNamePrefix"/> resolves to the NAS "opened, not saved" area — neither <c>recipes/</c> (New) nor
    /// <c>scenarios/</c> (Open), so no list offers it. The master's prefetch fills it (<c>AssetInventoryProcessManager.StageRecordedScenario</c>).
    /// ⛔ The name never reaches the original scenario folder nor the recording.
    /// </summary>
    public static class RecordedScenarioNames
    {
        private const string Prefix = ContentLayout.OpenedNamePrefix + ContentLayout.OpenedRecordedFolder + "/";

        /// <summary>The scenario name that opens the scenario copy of exercise <paramref name="exerciseId"/>.</summary>
        public static string ForExercise(Guid exerciseId)
            => ContentLayout.OpenedCopyName(ContentLayout.OpenedRecordedFolder, exerciseId.ToString());

        /// <summary>The NAS folder the opened copy of <paramref name="exerciseId"/> is staged at (<c>{nas}/opened/recorded/&lt;id&gt;</c>).</summary>
        public static string DirectoryFor(string nasRoot, Guid exerciseId)
            => ContentLayout.ResolveScenarioDirectory(nasRoot, ForExercise(exerciseId));

        /// <summary>The exercise a scenario name opens the copy of, or false when <paramref name="scenarioName"/> is not such a name.</summary>
        public static bool TryParse(string? scenarioName, out Guid exerciseId)
        {
            exerciseId = Guid.Empty;
            if (string.IsNullOrEmpty(scenarioName)) return false;
            var name = scenarioName!.Replace('\\', '/');
            return name.StartsWith(Prefix, StringComparison.Ordinal)
                   && Guid.TryParse(name.Substring(Prefix.Length), out exerciseId)
                   && exerciseId != Guid.Empty;
        }
    }
}
