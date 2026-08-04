using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Editor.AiShared.Catalog;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Hashing;
using Hrot.MuscleCharacter.Animation.Nodes;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Custom drawer for PlayMontageChainNode (AiPrimitive params struct).
/// Renders a reorderable montage chain UI with add/remove/move controls.
///
/// DISPATCH KEYING (Route A - Node-level):
/// This drawer uses Route A dispatch keying: it inspects the node to determine if it's
/// hosting a PlayMontageChainNode AiPrimitive. The drawer is registered by calling
/// Handles() on all registered drawers, which checks if the node contains the target
/// primitive (via reflection of the node's params struct type).
///
/// Alternative (Route B) would be a field-level [MontageChainPicker] attribute on
/// ChainedMontages, but Route A is chosen for consistency with WhenNodeDrawer pattern
/// and explicitness in the drawer registry.
/// </summary>
public sealed class PlayMontageChainNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IAnimationTkbQueries _animationQueries;
    private readonly IEditService _editService;
    private readonly Func<string?> _currentClassProvider;

    public PlayMontageChainNodeDrawer(
        IAnimationTkbQueries animationQueries,
        IEditService editService,
        Func<string?> currentClassProvider)
    {
        _animationQueries     = animationQueries     ?? throw new ArgumentNullException(nameof(animationQueries));
        _editService          = editService          ?? throw new ArgumentNullException(nameof(editService));
        _currentClassProvider = currentClassProvider ?? throw new ArgumentNullException(nameof(currentClassProvider));
    }

    /// <summary>
    /// Recognize a PlayMontageChainNode by checking if the node is an AiPrimitive
    /// hosting PlayMontageChainNode params. This is Route A dispatch keying.
    /// </summary>
    public bool Handles(Node node)
    {
        if (node == null) return false;
        
        // For now, accept any node that might host this primitive.
        // In full implementation, would inspect node.GetType().Name or check
        // for AiPrimitive attribute indicating PlayMontageChainNode hosting.
        // This is a placeholder that will be refined when the AiPrimitive node
        // container type is identified.
        
        // Check if this is named PlayMontageChainNode or similar
        var nodeType = node.GetType();
        return nodeType.Name.Contains("PlayMontageChainNode") || 
               nodeType.Name.Contains("AiPrimitiveNode");
    }

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new PlayMontageChainNodeSession(
            node, parentAsset,
            _animationQueries, _editService, _currentClassProvider);
}

/// <summary>
/// Session for editing PlayMontageChainNode parameters.
/// Manages the working copy of chain state, dirty tracking, and write-back via IEditService.
/// 
/// Storage-agnostic write-back: Works whether ChainedMontages is int[] (current) or 
/// [InlineArray(8)] (future per DEBT D-18). Uses Span-cast pattern for forward compatibility.
/// </summary>
internal sealed class PlayMontageChainNodeSession : INodeEditSession
{
    private readonly Node _node;
    private readonly BlueprintAsset _parent;
    private readonly IAnimationTkbQueries _animationQueries;
    private readonly IEditService _editService;
    private readonly Func<string?> _currentClassProvider;

    // Working copy of chain state (mirrors node.ChainedMontages + ChainCount)
    private byte _chainCount;
    private int[] _chainedMontages = new int[8];

    public bool IsDirty { get; private set; }

    public PlayMontageChainNodeSession(
        Node node,
        BlueprintAsset parentAsset,
        IAnimationTkbQueries animationQueries,
        IEditService editService,
        Func<string?> currentClassProvider)
    {
        _node                 = node ?? throw new ArgumentNullException(nameof(node));
        _parent               = parentAsset ?? throw new ArgumentNullException(nameof(parentAsset));
        _animationQueries     = animationQueries ?? throw new ArgumentNullException(nameof(animationQueries));
        _editService          = editService ?? throw new ArgumentNullException(nameof(editService));
        _currentClassProvider = currentClassProvider ?? throw new ArgumentNullException(nameof(currentClassProvider));

        // Initialize working copy from node's current state
        LoadFromNode();
    }

    public void Draw()
    {
        ImGui.Text("Play Montage Chain");
        ImGui.Separator();

        var currentClass = _currentClassProvider?.Invoke();
        if (string.IsNullOrEmpty(currentClass))
        {
            ImGui.TextColored(EditorColors.Warning, "⚠ No target class context available");
            return;
        }

        DrawChainUI(currentClass);
        ImGui.Separator();

        // ANC-P5-08c: Draw validation feedback for ANIM005 and ANIM012
        DrawValidationFeedback(currentClass);
        ImGui.Separator();

        if (IsDirty)
        {
            WriteBackToNode();
        }
    }

    public void ResetDirty()
    {
        IsDirty = false;
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    // ── Internal Methods (Test Hooks) ────────────────────────────────────────────

    /// <summary>
    /// Test hook: Add an entry to the chain (up to 8 entries).
    /// Sets IsDirty if an entry was actually added.
    /// </summary>
    internal void AddChainEntry()
    {
        if (_chainCount < 8)
        {
            _chainCount++;
            // New entry defaults to 0
            IsDirty = true;
        }
    }

    /// <summary>
    /// Test hook: Remove an entry from the chain at the given index.
    /// Shifts remaining entries and zeroes the tail.
    /// </summary>
    internal void RemoveChainEntry(int index)
    {
        if (index >= 0 && index < _chainCount)
        {
            // Shift entries up
            for (int i = index; i < _chainCount - 1; i++)
            {
                _chainedMontages[i] = _chainedMontages[i + 1];
            }
            // Zero the tail
            _chainedMontages[_chainCount - 1] = 0;
            _chainCount--;
            IsDirty = true;
        }
    }

    /// <summary>
    /// Test hook: Move an entry up in the chain (swap with previous).
    /// </summary>
    internal void MoveChainEntryUp(int index)
    {
        if (index > 0 && index < _chainCount)
        {
            // Swap
            (_chainedMontages[index - 1], _chainedMontages[index]) = 
                (_chainedMontages[index], _chainedMontages[index - 1]);
            IsDirty = true;
        }
    }

    /// <summary>
    /// Test hook: Move an entry down in the chain (swap with next).
    /// </summary>
    internal void MoveChainEntryDown(int index)
    {
        if (index >= 0 && index < _chainCount - 1)
        {
            // Swap
            (_chainedMontages[index], _chainedMontages[index + 1]) = 
                (_chainedMontages[index + 1], _chainedMontages[index]);
            IsDirty = true;
        }
    }

    /// <summary>
    /// Test hook: Set montage ID for an entry.
    /// </summary>
    internal void SetChainMontageId(int index, int montageId)
    {
        if (index >= 0 && index < _chainCount)
        {
            _chainedMontages[index] = montageId;
            IsDirty = true;
        }
    }

    /// <summary>
    /// Test hook: Get current chain count.
    /// </summary>
    internal byte GetChainCount() => _chainCount;

    /// <summary>
    /// Test hook: Get montage ID at index (0 if out of bounds or beyond ChainCount).
    /// </summary>
    internal int GetChainMontageId(int index)
    {
        if (index >= 0 && index < _chainCount)
            return _chainedMontages[index];
        return 0;
    }

    /// <summary>
    /// Test hook: Verify that entries beyond ChainCount are zeroed (tail verification).
    /// </summary>
    internal bool VerifyTailZeroed()
    {
        for (int i = _chainCount; i < 8; i++)
        {
            if (_chainedMontages[i] != 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Test hook: Get the full array (for round-trip testing).
    /// </summary>
    internal int[] GetChainedMontages() => (int[])_chainedMontages.Clone();

    /// <summary>
    /// Test hook: Get validation feedback for ANIM005 (same-slot requirement).
    /// Returns null if all entries share the same slot, or an error message if not.
    /// </summary>
    internal string? GetANIM005ValidationFeedback(string currentClass)
    {
        if (_chainCount <= 1) return null; // 0 or 1 entries can't have a slot conflict

        var slots = new HashSet<byte>();
        for (int i = 0; i < _chainCount; i++)
        {
            var montageId = _chainedMontages[i];
            if (montageId == 0) continue; // Unset entry, skip

            // Resolve montage name from ID (need to iterate through available montages)
            var montages = _animationQueries.GetPlayableMontages(currentClass);
            var montage = montages.FirstOrDefault(m => 
                StableIdHasher.ComputeMontageAssetId(m.Name) == montageId);

            if (montage != null)
            {
                slots.Add(montage.Slot);
            }
        }

        if (slots.Count > 1)
        {
            var slotList = string.Join(", ", slots);
            return $"❌ ANIM005 Violation: Chain entries must use the same animation slot. Found slots: [{slotList}]";
        }

        return null;
    }

    /// <summary>
    /// Test hook: Get validation feedback for ANIM012 (length ≤ 8).
    /// Returns null if chain length is valid, or a warning message if over-length.
    /// </summary>
    internal string? GetANIM012ValidationFeedback()
    {
        if (_chainCount > 8)
        {
            return $"⚠️ ANIM012 Warning: Chain length ({_chainCount}) exceeds maximum of 8. Loaded asset may have been edited externally.";
        }
        return null;
    }

    /// <summary>
    /// Test hook: Truncate the chain to 8 entries (removes entries beyond index 7).
    /// Sets IsDirty if truncation occurred.
    /// </summary>
    internal void TruncateChainTo8()
    {
        if (_chainCount > 8)
        {
            _chainCount = 8;
            // Zero entries beyond 8
            for (int i = 8; i < _chainedMontages.Length; i++)
            {
                _chainedMontages[i] = 0;
            }
            IsDirty = true;
        }
    }

    // ── Private Methods ──────────────────────────────────────────────────────────

    private void LoadFromNode()
    {
        // Initialize to empty before any reflection attempt.
        _chainCount = 0;
        Array.Clear(_chainedMontages, 0, 8);
        IsDirty = false;

        // Storage-agnostic read: locate ChainCount + ChainedMontages on the node
        // (direct fields) or on a nested params struct (e.g. `Params`/`Body`),
        // accepting both managed `int[]` and `[InlineArray(8)] int _e0`
        // (DEBT D-18). Read via Span<int> so both storages work uniformly.
        var (countAccess, montagesAccess) = ResolveChainAccess(_node);
        if (countAccess is null || montagesAccess is null) return;

        _chainCount = countAccess.Read();
        var srcSpan = montagesAccess.AsSpan();
        int n = Math.Min(_chainCount, Math.Min(srcSpan.Length, _chainedMontages.Length));
        for (int i = 0; i < n; i++) _chainedMontages[i] = srcSpan[i];
    }

    private void DrawChainUI(string currentClass)
    {
        // Get available montages for dropdown
        var montages = _animationQueries.GetPlayableMontages(currentClass);
        var montageNames = montages.Select(m => m.Name).ToArray();

        // Draw reorderable list of entries
        ImGui.Text($"Chain entries: {_chainCount}/8");
        ImGui.Separator();

        // Draw each entry
        for (int i = 0; i < _chainCount; i++)
        {
            ImGui.PushID(i);
            
            var montageId = _chainedMontages[i];
            
            // Find and display montage name
            string displayName = "None";
            if (montageId != 0 && montages.Any())
            {
                var montage = montages.FirstOrDefault(m => 
                    StableIdHasher.ComputeMontageAssetId(m.Name) == montageId);
                if (montage != null)
                    displayName = montage.Name;
            }

            // Montage selector combo
            int selectedIdx = Array.IndexOf(montageNames, displayName);
            if (selectedIdx < 0) selectedIdx = 0;

            if (ImGui.Combo($"##Montage{i}", ref selectedIdx, montageNames, montageNames.Length))
            {
                if (selectedIdx >= 0 && selectedIdx < montageNames.Length)
                {
                    var selectedMontage = montages[selectedIdx];
                    var newId = StableIdHasher.ComputeMontageAssetId(selectedMontage.Name);
                    SetChainMontageId(i, newId);
                }
            }

            // Move up button
            ImGui.SameLine();
            ImGui.BeginDisabled(i == 0);
            if (ImGui.Button("↑"))
            {
                MoveChainEntryUp(i);
            }
            ImGui.EndDisabled();
            
            // Move down button
            ImGui.SameLine();
            ImGui.BeginDisabled(i == _chainCount - 1);
            if (ImGui.Button("↓"))
            {
                MoveChainEntryDown(i);
            }
            ImGui.EndDisabled();
            
            // Remove button
            ImGui.SameLine();
            if (ImGui.Button("✕"))
            {
                RemoveChainEntry(i);
                ImGui.PopID();
                continue;
            }
            
            ImGui.PopID();
        }

        ImGui.Separator();

        // Add button
        if (_chainCount < 8)
        {
            if (ImGui.Button("Add Entry"))
            {
                AddChainEntry();
            }
        }
        else
        {
            ImGui.TextDisabled("Chain full (8/8)");
        }
    }

    private void WriteBackToNode()
    {
        // Storage-agnostic write-back (Addendum A 08b, DEBT D-18):
        // Whether ChainedMontages is `int[]` (current) or `[InlineArray(8)] int _e0`
        // (future), we mutate via Span<int>. Tail entries beyond ChainCount are zeroed
        // to keep the component state clean (matches DD-5 §3.5 ClearMontageQueueNode
        // convention).
        var (countAccess, montagesAccess) = ResolveChainAccess(_node);
        if (countAccess is null || montagesAccess is null)
        {
            // Container shape unknown — at minimum signal the asset is dirty so the
            // editor's save-on-close still fires, and surface a no-op rather than
            // silently losing the edit.
            _editService.MarkDirty(_parent);
            return;
        }

        // BP-11: snapshot the STORED chain before overwriting it — that is the undo baseline.
        var  dst         = montagesAccess.AsSpan();
        byte beforeCount = countAccess.Read();
        var  beforeItems = dst.ToArray();

        // Build the chain the working state implies (tail zeroed so old entries past the new
        // ChainCount don't leak — matches DD-5 §3.5 ClearMontageQueueNode convention).
        var afterItems = new int[dst.Length];
        int n = Math.Min(_chainCount, Math.Min(dst.Length, _chainedMontages.Length));
        for (int i = 0; i < n; i++) afterItems[i] = _chainedMontages[i];
        byte afterCount = _chainCount;

        // This runs every frame while IsDirty, so a no-diff write must not push an undo entry —
        // otherwise a single add-montage click would fill the stack for as long as the panel is open.
        if (beforeCount == afterCount && beforeItems.AsSpan().SequenceEqual(afterItems))
            return;

        _editService.RecordPropertyEdit(
            _parent, "Edit Montage Chain",
            apply: () => ApplyChain(afterCount,  afterItems),
            undo:  () => ApplyChain(beforeCount, beforeItems));
    }

    /// <summary>
    /// BP-11: writes a chain into the node's storage <b>and</b> back into this session's working
    /// copy. Both halves matter — <see cref="WriteBackToNode"/> re-runs every frame while
    /// <see cref="IsDirty"/>, so an undo that only restored the node would be immediately overwritten
    /// by the stale working state on the next frame.
    /// </summary>
    private void ApplyChain(byte count, int[] items)
    {
        var (countAccess, montagesAccess) = ResolveChainAccess(_node);
        if (countAccess is null || montagesAccess is null) return;

        var dst = montagesAccess.AsSpan();
        int n   = Math.Min(items.Length, dst.Length);
        for (int i = 0; i < n; i++) dst[i] = items[i];
        for (int i = n; i < dst.Length; i++) dst[i] = 0;
        countAccess.Write(count);

        _chainCount = count;
        for (int i = 0; i < _chainedMontages.Length; i++)
            _chainedMontages[i] = i < items.Length ? items[i] : 0;
    }

    // ── Reflection-backed accessors (storage-agnostic) ────────────────────────────

    /// <summary>
    /// Locate ChainCount and ChainedMontages on the node, looking first at the node
    /// directly and then at a nested params/body struct field. Works for both
    /// `int[] ChainedMontages` (current) and `[InlineArray(8)] int _e0` (DEBT D-18 future).
    /// Returns (null, null) if the container shape is unknown — callers fall back
    /// to a MarkDirty-only path.
    /// </summary>
    private static (IChainCountAccess?, IChainMontagesAccess?) ResolveChainAccess(object container)
    {
        if (container is null) return (null, null);

        // Try direct fields on the container first.
        var (cnt, mont) = TryResolveOn(container);
        if (cnt is not null && mont is not null) return (cnt, mont);

        // Then try common nested wrappers (Params/Body/Node/Primitive). Only
        // reference-type nesteds propagate writes naturally; value-type nesteds
        // would need a box-and-restore step (left as a TODO until a real
        // container of that shape appears in the codebase).
        foreach (var nestedName in new[] { "Params", "Body", "Node", "Primitive", "Inner" })
        {
            var nestedField = container.GetType().GetField(nestedName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (nestedField is null || nestedField.FieldType.IsValueType) continue;
            var nested = nestedField.GetValue(container);
            if (nested is null) continue;
            var (c2, m2) = TryResolveOn(nested);
            if (c2 is not null && m2 is not null) return (c2, m2);
        }

        return (null, null);
    }

    private static (IChainCountAccess?, IChainMontagesAccess?) TryResolveOn(object obj)
    {
        var t = obj.GetType();
        var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        var countField = t.GetField("ChainCount", bf);
        var montagesField = t.GetField("ChainedMontages", bf);
        if (countField is null || montagesField is null) return (null, null);

        IChainCountAccess countAccess = new FieldCountAccess(obj, countField);

        // Currently PlayMontageChainNode declares `int[] ChainedMontages`.
        // If/when DEBT D-18 converts to `[InlineArray(8)] int _e0`, add a
        // dedicated InlineArrayMontagesAccess here (Pattern A — Span-cast).
        if (montagesField.FieldType != typeof(int[])) return (null, null);
        IChainMontagesAccess montagesAccess = new ManagedArrayMontagesAccess(obj, montagesField);
        return (countAccess, montagesAccess);
    }

    private interface IChainCountAccess { byte Read(); void Write(byte v); }
    private interface IChainMontagesAccess { Span<int> AsSpan(); }

    private sealed class FieldCountAccess : IChainCountAccess
    {
        private readonly object _owner; private readonly System.Reflection.FieldInfo _f;
        public FieldCountAccess(object o, System.Reflection.FieldInfo f) { _owner = o; _f = f; }
        public byte Read() => Convert.ToByte(_f.GetValue(_owner));
        public void Write(byte v)
        {
            // Coerce to the field's declared type (byte / int / uint).
            object boxed = _f.FieldType == typeof(byte) ? v
                          : _f.FieldType == typeof(int)  ? (object)(int)v
                          : _f.FieldType == typeof(uint) ? (object)(uint)v
                          : v;
            _f.SetValue(_owner, boxed);
        }
    }

    private sealed class ManagedArrayMontagesAccess : IChainMontagesAccess
    {
        private readonly object _owner; private readonly System.Reflection.FieldInfo _f;
        public ManagedArrayMontagesAccess(object o, System.Reflection.FieldInfo f) { _owner = o; _f = f; }
        public Span<int> AsSpan()
        {
            var arr = (int[]?)_f.GetValue(_owner);
            if (arr is null) { arr = new int[8]; _f.SetValue(_owner, arr); }
            else if (arr.Length < 8) { var grown = new int[8]; Array.Copy(arr, grown, arr.Length); arr = grown; _f.SetValue(_owner, arr); }
            return arr.AsSpan();
        }
    }


    private void DrawValidationFeedback(string currentClass)
    {
        ImGui.Text("Validation:");
        ImGui.Separator();

        // ANIM005: Check for same-slot requirement
        var anim005Feedback = GetANIM005ValidationFeedback(currentClass);
        if (anim005Feedback != null)
        {
            ImGui.TextColored(EditorColors.Error, anim005Feedback);
        }

        // ANIM012: Check for length ≤ 8
        var anim012Feedback = GetANIM012ValidationFeedback();
        if (anim012Feedback != null)
        {
            ImGui.TextColored(EditorColors.Warning, anim012Feedback);
            ImGui.SameLine();
            if (ImGui.Button("Truncate to 8"))
            {
                TruncateChainTo8();
            }
        }

        if (anim005Feedback == null && anim012Feedback == null)
        {
            ImGui.TextColored(EditorColors.Info, "✓ No validation errors");
        }
    }
}
