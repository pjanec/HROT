using System;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐ <c>CE-4091</c> (AQ108 D7, D22) — the NAME under which a recording's scenario copy is opened for editing:
    /// <c>Recipes/Recorded/&lt;exerciseId&gt;</c>. ⭐ A scenario load is a NAME every node resolves against its own scenarios root (nothing
    /// accepts a path), and a name under the reserved <see cref="RecipesFolder"/> is how "New from a recipe" already opens a scenario that
    /// has no file of its own — so a recorded copy is opened the same way, by a name the master's prefetch resolves to the copy
    /// (<c>AssetInventoryProcessManager.StageRecordedScenario</c>). ⛔ The name never reaches the original scenario folder nor the recording.
    /// </summary>
    public static class RecordedScenarioNames
    {
        /// <summary>The reserved subfolder of the scenarios root that holds recipes (<c>ScenarioNewAssetService.SeedSubfolder</c> is this).</summary>
        public const string RecipesFolder = "Recipes";

        /// <summary>The subfolder of <see cref="RecipesFolder"/> that holds the opened copies of recordings.</summary>
        public const string RecordedFolder = "Recorded";

        private const string Prefix = RecipesFolder + "/" + RecordedFolder + "/";

        /// <summary>The scenario name that opens the scenario copy of exercise <paramref name="exerciseId"/>.</summary>
        public static string ForExercise(Guid exerciseId) => Prefix + exerciseId.ToString();

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
