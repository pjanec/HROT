using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using ImGuiNET;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// BP-12c — ImGui modal for declaring a blueprint custom event: a name field plus an editable
/// parameter list (name + type per row).
///
/// <para>
/// Parameters are part of the create gesture rather than a follow-up edit because they are what the
/// declaration is <em>for</em>: <c>NodePinSchema.CallCustomEventPins</c> projects one data-in pin per
/// parameter onto every <c>CallCustomEvent</c> node, and the BP-07 picker labels an event by its
/// parameter names. A name-only modal would only ever produce events that carry nothing.
/// </para>
///
/// <para>
/// Mirrors <see cref="VariableCreateModal"/>: this class owns transient UI state only, the create
/// work is delegated to a caller-supplied callback so the path stays headless-testable
/// (<see cref="Host.BlueprintDocumentFactory.CreateCustomEvent"/>), and every ImGui call is gated
/// behind a current-context check so the type is safe to instantiate in tests. The validation rules
/// live in <see cref="ValidationMessage"/> so they can be asserted without an ImGui context.
/// </para>
/// </summary>
public sealed class CustomEventCreateModal
{
    private const string PopupId = "Create Custom Event##bp_create_evt";

    /// <summary>Confirm payload: the event name and its ordered <c>(name, typeId)</c> parameters.</summary>
    public delegate void ConfirmHandler(string name, IReadOnlyList<(string Name, string TypeId)> parameters);

    private readonly ConfirmHandler  _onConfirm;
    private readonly BlueprintAsset? _asset;

    private bool   _openRequested;
    private string _name = "NewEvent";

    // One row per declared parameter; the type is held as an index into SelectableTypeIds so the
    // combo and the payload cannot drift.
    private readonly List<(string Name, int TypeIndex)> _parameters = new();

    /// <param name="onConfirm">
    /// Invoked with <c>(name, parameters)</c> when the user confirms. Wire this to
    /// <see cref="Host.BlueprintDocumentFactory.CreateCustomEvent"/>.
    /// </param>
    /// <param name="asset">
    /// The owning asset, used to reject a name that collides with an existing custom event. May be
    /// <see langword="null"/> in tests, in which case duplicate-checking is skipped.
    /// </param>
    public CustomEventCreateModal(ConfirmHandler onConfirm, BlueprintAsset? asset = null)
    {
        _onConfirm = onConfirm ?? throw new ArgumentNullException(nameof(onConfirm));
        _asset     = asset;
    }

    /// <summary>
    /// Requests the modal to open on the next <see cref="Draw"/> call, resetting name and
    /// parameters. Call this from the My Blueprint "Custom Events +" command.
    /// </summary>
    public void Open()
    {
        _name = "NewEvent";
        _parameters.Clear();
        _openRequested = true;
    }

    /// <summary>
    /// Draws the modal if open. No-op when there is no current ImGui context (headless).
    /// Must be called every frame from the owning window's draw routine.
    /// </summary>
    public void Draw()
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
            return;

        if (_openRequested)
        {
            ImGui.OpenPopup(PopupId);
            _openRequested = false;
        }

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var typeIds = BlueprintTypeSystem.SelectableTypeIds;

        ImGui.TextUnformatted("Name");
        ImGui.SetNextItemWidth(260f);
        ImGui.InputText("##bp_evt_name", ref _name, 128);

        ImGui.Separator();
        ImGui.TextUnformatted("Parameters");

        // Materialised index loop: a row can remove itself mid-draw.
        int removeAt = -1;
        for (int i = 0; i < _parameters.Count; i++)
        {
            ImGui.PushID(i);

            var (pName, pTypeIndex) = _parameters[i];

            ImGui.SetNextItemWidth(140f);
            if (ImGui.InputText("##pname", ref pName, 128))
                _parameters[i] = (pName, pTypeIndex);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(110f);
            var typeId = typeIds[Math.Clamp(pTypeIndex, 0, typeIds.Count - 1)];
            if (ImGui.BeginCombo("##ptype", ShortName(typeId)))
            {
                for (int t = 0; t < typeIds.Count; t++)
                {
                    bool selected = t == pTypeIndex;
                    if (ImGui.Selectable(ShortName(typeIds[t]), selected))
                        _parameters[i] = (pName, t);
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("x")) removeAt = i;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove this parameter");

            ImGui.PopID();
        }
        if (removeAt >= 0) _parameters.RemoveAt(removeAt);

        if (ImGui.SmallButton("+ Parameter"))
            _parameters.Add((MakeUniqueParameterName(), 0));

        // Live validation. Every rule here is also enforced by CreateCustomEvent, which is the
        // authoritative guard — this is the half that tells the user *why* before they click.
        var problem = ValidationMessage(_asset, _name, Payload(typeIds));

        if (problem is not null)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.95f, 0.55f, 0.20f, 1f), problem);
        }
        else
        {
            // BP-24: the body Event graph is created with the declaration and the canvas opens
            // on it (the compiler emits Event_{Name} from that graph).
            ImGui.TextDisabled("Creates the event and its body graph; the canvas will open on the");
            ImGui.TextDisabled("body. Call it with a Call Custom Event node from any graph here.");
        }

        ImGui.Separator();

        bool canCreate = problem is null;
        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Create", new System.Numerics.Vector2(100, 0)))
        {
            _onConfirm(_name, Payload(typeIds));
            ImGui.CloseCurrentPopup();
        }
        if (!canCreate) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(100, 0)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    // ── Headless helpers ──────────────────────────────────────────────────────

    private IReadOnlyList<(string Name, string TypeId)> Payload(IReadOnlyList<string> typeIds)
        => _parameters
            .Select(p => (p.Name, typeIds[Math.Clamp(p.TypeIndex, 0, typeIds.Count - 1)]))
            .ToList();

    private string MakeUniqueParameterName()
    {
        var taken = new HashSet<string>(_parameters.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains("Param")) return "Param";
        for (int i = 1; ; i++)
            if (!taken.Contains($"Param{i}")) return $"Param{i}";
    }

    /// <summary>
    /// The single reason Confirm is disabled, or <see langword="null"/> when the declaration is
    /// valid. One message at a time, in the order a designer meets them.
    /// <para>Exposed <c>internal</c> so the rules are testable without an ImGui context.</para>
    /// </summary>
    internal static string? ValidationMessage(
        BlueprintAsset? asset,
        string          name,
        IReadOnlyList<(string Name, string TypeId)> parameters)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name cannot be empty.";
        if (!BlueprintDocumentFactory.IsValidDeclarationName(name))
            return $"'{name.Trim()}' is not a valid name — letters, digits and _ only, "
                 + "and it cannot start with a digit or be a C# keyword.";
        if (asset is not null && BlueprintDocumentFactory.IsDuplicateCustomEventName(asset, name))
            return $"A custom event named '{name.Trim()}' already exists.";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parameters)
        {
            if (string.IsNullOrWhiteSpace(p.Name))
                return "A parameter has no name.";
            if (!BlueprintDocumentFactory.IsValidDeclarationName(p.Name))
                return $"Parameter '{p.Name.Trim()}' is not a valid name — letters, digits and _ "
                     + "only, and it cannot start with a digit or be a C# keyword.";
            if (!seen.Add(p.Name.Trim()))
                return $"Two parameters are both named '{p.Name.Trim()}'.";
        }

        return null;
    }

    private static string ShortName(string typeId)
    {
        var dot = typeId.LastIndexOf('.');
        return dot >= 0 && dot < typeId.Length - 1 ? typeId[(dot + 1)..] : typeId;
    }
}
