namespace Hrot.Editor;

/// <summary>
/// Thin adapter that bridges <see cref="IEditorLogic"/> to
/// <see cref="IScenarioCreationSession"/> for use by
/// <see cref="ScenarioNewAssetService"/> in the Save-As / New Asset flows.
/// </summary>
/// <remarks>
/// <see cref="IEditorLogic"/> already defines <c>NewScenario</c>,
/// <c>SaveScenarioAs</c>, and <c>LoadScenarioByName</c> — this adapter
/// simply forwards those calls. Created for DEC-9 (BATCH-20).
/// </remarks>
internal sealed class EditorLogicSessionAdapter : IScenarioCreationSession
{
    private readonly IEditorLogic _editorLogic;

    public EditorLogicSessionAdapter(IEditorLogic editorLogic)
    {
        _editorLogic = editorLogic ?? throw new ArgumentNullException(nameof(editorLogic));
    }

    public void NewScenario()
        => _editorLogic.NewScenario();

    public void SaveScenarioAs(string scenarioName)
        => _editorLogic.SaveScenarioAs(scenarioName);

    public void LoadScenarioByName(string scenarioName)
        => _editorLogic.LoadScenarioByName(scenarioName);
}
