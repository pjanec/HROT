using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐ <b>A minimal <see cref="IBlackboardManagedAsset"/> for composition-root rails.</b>
///
/// <para>⚠ <b>Why a stand-in and not a real asset, stated once here rather than in every rail</b>
/// *(handoff: say which layer is faked)*: <c>HsmAsset</c>'s constructor is internal and reachable only
/// through the DTO mapper, and <c>BehaviorTreeAsset</c>'s needs a whole <c>Fbt</c> blob. ⭐ Every
/// production path these rails exercise is typed on the INTERFACE, which the real assets and this
/// satisfy identically.</para>
///
/// <para>⭐ <see cref="UpdateVariableDefaultValueJson"/> is REAL — it stores, so a rail can assert the
/// designer's edit actually landed rather than that a call was made.</para>
/// </summary>
internal sealed class TestManagedAsset : IEditableAsset, IBlackboardManagedAsset
{
    private readonly List<BlackboardVariableEntry> _vars;

    public TestManagedAsset(AssetKind kind, params BlackboardVariableEntry[] vars)
    {
        Kind  = kind;
        _vars = vars.ToList();
    }

    public Guid AssetId { get; } = Guid.NewGuid();
    public string Name => "DialogHost";
    public AssetKind Kind { get; }
    public string SourceFilePath => "/dialog-host.json";
    public bool IsDirty => false;
    public bool IsEditorOwned => true;
    public event Action? Changed { add { } remove { } }

    public bool IsBlackboardEditorManaged => true;
    public void SetBlackboardEditorManaged(bool managed) { }
    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;
    public void AddVariable(BlackboardVariableEntry entry) => _vars.Add(entry);
    public void RemoveVariable(string name) => _vars.RemoveAll(v => v.Name == name);
    public void RemoveVariables(IReadOnlyList<string> names) { }
    public void UpdateVariableComment(string name, string? comment) { }

    /// <summary>⭐ Stores, so "the edit landed" is observable.</summary>
    public void UpdateVariableDefaultValueJson(string name, string? json)
    {
        for (int i = 0; i < _vars.Count; i++)
            if (_vars[i].Name == name) _vars[i] = _vars[i] with { DefaultValueJson = json };
    }

    public void MoveVariable(int sourceIndex, int destIndex) { }
    public void RenameVariable(string oldName, string newName) { }
    public int CountNodesReferencingVariable(string name) => 0;
    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName)
        => Array.Empty<BlackboardAliasBinding>();
    public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
    public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
}
