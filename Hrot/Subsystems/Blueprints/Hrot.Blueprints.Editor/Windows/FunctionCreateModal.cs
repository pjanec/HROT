using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using ImGuiNET;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// BP-24 — ImGui modal for creating a graph: a single name field.
///
/// <para>
/// ⭐ BP-77: parameterised by <b>noun</b> rather than copied for Macro. The two dialogs differ only
/// in a title and a default name; a second class would be two hand-maintained copies of the same
/// validation, which is exactly what <see cref="ParameterRowsView"/> was extracted to stop.
/// </para>
///
/// <para>
/// Name-only on purpose: a function's signature lives on the graph and is edited in the Graph
/// Signature window, which already does full inputs/outputs CRUD — duplicating that grid here
/// would drift from it. Mirrors <see cref="CustomEventCreateModal"/>: transient UI state only,
/// the create work is a caller-supplied callback
/// (<see cref="Host.BlueprintDocumentFactory.CreateFunctionGraph"/>), every ImGui call is gated
/// behind a current-context check, and the validation rules live in
/// <see cref="ValidationMessage"/> so they are assertable without an ImGui context.
/// </para>
/// </summary>
public sealed class FunctionCreateModal
{
    private readonly string          _popupId;
    private readonly string          _defaultName;
    private readonly Action<string>  _onConfirm;
    private readonly BlueprintAsset? _asset;

    private bool   _openRequested;
    private string _name;
    private bool   _focusPending;

    /// <param name="onConfirm">
    /// Invoked with the confirmed name. Wire this to
    /// <see cref="Host.BlueprintDocumentFactory.CreateFunctionGraph"/>.
    /// </param>
    /// <param name="asset">
    /// The owning asset, used to reject a name an existing graph already holds. May be
    /// <see langword="null"/> in tests, in which case duplicate-checking is skipped.
    /// </param>
    /// <param name="noun">
    /// BP-77 — what is being created: <c>"Function"</c> (the default, so every existing caller is
    /// unchanged) or <c>"Macro"</c>. Drives the window title, the popup id and the default name.
    /// </param>
    public FunctionCreateModal(
        Action<string> onConfirm, BlueprintAsset? asset = null, string noun = "Function")
    {
        _onConfirm   = onConfirm ?? throw new ArgumentNullException(nameof(onConfirm));
        _asset       = asset;
        _popupId     = $"Create {noun}##bp_create_{noun.ToLowerInvariant()}";
        _defaultName = "New" + noun;
        _name        = _defaultName;
    }

    /// <summary>Requests the modal on the next <see cref="Draw"/>, with a fresh default name.</summary>
    public void Open()
    {
        _name          = _defaultName;
        _openRequested = true;
        _focusPending  = true;
    }

    /// <summary>Draws the modal if open. No-op when there is no ImGui context (headless).</summary>
    public void Draw()
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
            return;

        if (_openRequested)
        {
            ImGui.OpenPopup(_popupId);
            _openRequested = false;
        }

        bool open = true;
        if (!ImGui.BeginPopupModal(_popupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted("Name");
        if (_focusPending)
        {
            ImGui.SetKeyboardFocusHere();
            _focusPending = false;
        }
        ImGui.SetNextItemWidth(260f);
        bool submitted = ImGui.InputText(
            "##bp_fn_name", ref _name, 128, ImGuiInputTextFlags.EnterReturnsTrue);

        var problem = ValidationMessage(_asset, _name);
        if (problem is not null)
            ImGui.TextColored(new System.Numerics.Vector4(0.95f, 0.55f, 0.20f, 1f), problem);
        else
            ImGui.TextDisabled("Inputs and outputs are edited in the Graph Signature window.");

        ImGui.Separator();

        bool canCreate = problem is null;
        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Create", new System.Numerics.Vector2(100, 0)) || (submitted && canCreate))
        {
            _onConfirm(_name);
            ImGui.CloseCurrentPopup();
        }
        if (!canCreate) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(100, 0)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    /// <summary>
    /// The single reason Confirm is disabled, or <see langword="null"/> when the name is valid.
    /// <para>Exposed <c>internal</c> so the rules are testable without an ImGui context.</para>
    /// </summary>
    internal static string? ValidationMessage(BlueprintAsset? asset, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name cannot be empty.";
        if (!BlueprintDocumentFactory.IsValidDeclarationName(name))
            return $"'{name.Trim()}' is not a valid name — letters, digits and _ only, "
                 + "and it cannot start with a digit or be a C# keyword.";
        if (asset is not null && BlueprintDocumentFactory.IsDuplicateGraphName(asset, name))
            return $"A graph named '{name.Trim()}' already exists.";
        return null;
    }
}
