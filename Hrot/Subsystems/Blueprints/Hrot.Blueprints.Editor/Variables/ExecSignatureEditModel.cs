using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Editor.Variables;

/// <summary>
/// BP-80 / BP-225 — headless edit model over a macro graph's <see cref="Graph.ExecInputs"/> or
/// <see cref="Graph.ExecOutputs"/>: the exec-pin half of <see cref="GraphSignatureEditModel"/>.
///
/// <para>
/// ⭐ <b>Why a separate model rather than generalising the parameter one.</b> Not the missing
/// <c>Type</c> — that alone would argue for a shared model with a suppressed column. It is that
/// <b>the mutations are not the same operations</b>: renaming an exec declaration moves a pin and
/// must carry its wires (<see cref="MacroExecPinMaintenance.Repoint"/>), and deleting one must take
/// its wires with it. <c>GraphSignatureEditModel</c> does none of that, and adding it there would
/// impose macro-only wire surgery on <c>ReturnNodeDrawer</c>'s Outputs table, which shares it.
/// </para>
///
/// <para>
/// ⚠ <b>The one thing copied deliberately</b> is the whole-list snapshot undo: <see cref="ExecInDecl"/>
/// is a mutable class and rename mutates in place, so a shallow <c>ToList()</c> would capture nothing.
/// Same reasoning as BP-89, same shape.
/// </para>
///
/// <para>
/// ⭐ <b>Reordering is safe and is NOT special-cased here.</b> A pin's identity is
/// <c>DeterministicIds.PinId(node, name, direction)</c>, and both the boundary node's pins and every
/// call site's are projected from this one list in this one order — so permuting it permutes both
/// sides together and no wire changes meaning. <see cref="MacroExecPinMaintenance"/>'s class docs
/// carry the full argument; the test carries the proof.
/// </para>
/// </summary>
public sealed class ExecSignatureEditModel
{
    private readonly BlueprintAsset _asset;
    private readonly Graph          _macro;
    private readonly bool           _isEntry;
    private readonly Action         _onChanged;
    private readonly Action<string, Action, Action>? _record;

    /// <param name="isEntry">
    /// <c>true</c> edits <see cref="Graph.ExecInputs"/> (entries); <c>false</c> edits
    /// <see cref="Graph.ExecOutputs"/> (exits).
    /// </param>
    /// <param name="record">
    /// Undo recorder, same seam as <see cref="GraphSignatureEditModel"/>'s. Null mutates directly.
    /// </param>
    public ExecSignatureEditModel(
        BlueprintAsset asset, Graph macro, bool isEntry, Action onChanged,
        Action<string, Action, Action>? record = null)
    {
        _asset     = asset     ?? throw new ArgumentNullException(nameof(asset));
        _macro     = macro     ?? throw new ArgumentNullException(nameof(macro));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _isEntry   = isEntry;
        _record    = record;
    }

    /// <summary>Live read-only view of the list being edited.</summary>
    public IReadOnlyList<ExecDeclView> Declarations
        => _isEntry
            ? _macro.ExecInputs .Select(d => new ExecDeclView(d.Id, d.Name)).ToList()
            : _macro.ExecOutputs.Select(d => new ExecDeclView(d.Id, d.Name)).ToList();

    /// <summary>Number of declarations. Cheaper than materialising <see cref="Declarations"/>.</summary>
    public int Count => _isEntry ? _macro.ExecInputs.Count : _macro.ExecOutputs.Count;

    /// <summary>
    /// Wires a delete of <paramref name="name"/> would remove. The rows view shows this before
    /// committing, so the cost is stated rather than discovered.
    /// </summary>
    public int WireCount(string name) => MacroExecPinMaintenance.CountWires(_asset, _macro, _isEntry, name);

    // ── Mutations ────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a declaration. ⚠ <b>A duplicate name is refused</b>, and that is the one genuinely
    /// corrupting edit available here: two declarations with the same name project to the SAME pin id
    /// (identity is <c>(node, name, direction)</c>), so the second silently collapses onto the first —
    /// two exec entries, one pin, and a splice that pairs index <c>k</c> against a pin two
    /// declarations claim. Returns false rather than throwing so the caller can report it.
    /// </summary>
    public bool AddDeclaration(string name)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        if (IsTaken(name, exceptId: null)) return false;

        var id = Guid.NewGuid();
        Mutate(Label("Add", name),
            apply: () => Insert(id, name),
            undo:  () => RemoveById(id));
        return true;
    }

    /// <summary>
    /// Removes a declaration <b>and the wires that referenced its pins</b>, everywhere it projected.
    /// ⛔ Leaving them behind is not an option: a link whose endpoint id no longer names a pin is a
    /// <i>dangling</i> link, which breaks the solution build with <c>BP1602</c> from a graph that
    /// looks intact on screen (BP-202).
    /// </summary>
    public void RemoveDeclaration(string name)
    {
        var index = IndexOfName(name);
        if (index < 0) return;
        var id = Declarations[index].Id;
        MacroExecPinMaintenance.PruneResult? pruned = null;

        Mutate(Label("Remove", name),
            apply: () =>
            {
                pruned = MacroExecPinMaintenance.Prune(_asset, _macro, _isEntry, name);
                RemoveById(id);
            },
            undo: () =>
            {
                Insert(id, name, index);
                MacroExecPinMaintenance.Restore(pruned);
            });
    }

    /// <summary>
    /// Renames a declaration and moves its wires with it. Refuses a name another declaration already
    /// holds (see <see cref="AddDeclaration"/> for why that would corrupt rather than merely annoy).
    /// </summary>
    public bool RenameDeclaration(string oldName, string newName)
    {
        if (newName == null) throw new ArgumentNullException(nameof(newName));
        var index = IndexOfName(oldName);
        if (index < 0 || oldName == newName) return false;
        var id = Declarations[index].Id;
        if (IsTaken(newName, exceptId: id)) return false;
        Mutate(LabelRename(oldName, newName),
            // ⭐ Repoint is its own inverse, so the undo is the reverse rename rather than a second
            // snapshot — and a snapshot could not have restored the WIRES anyway.
            apply: () => { SetName(id, newName); MacroExecPinMaintenance.Repoint(_asset, _macro, _isEntry, oldName, newName); },
            undo:  () => { SetName(id, oldName); MacroExecPinMaintenance.Repoint(_asset, _macro, _isEntry, newName, oldName); });
        return true;
    }

    /// <summary>
    /// Moves a declaration. ⭐ <b>No wire surgery, deliberately</b> — see the class docs: pins are
    /// name-keyed and both projections derive their order from this list, so a permutation moves both
    /// sides together.
    /// </summary>
    public void MoveDeclaration(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Count) return;
        if (toIndex   < 0 || toIndex   >= Count) return;
        if (fromIndex == toIndex) return;

        var before = Snapshot();
        Mutate($"Reorder macro {NounPlural}",
            apply: () => MoveAt(fromIndex, toIndex),
            undo:  () => RestoreFrom(before));
    }

    // ── Private ──────────────────────────────────────────────────────────────
    //
    // ⚠ ExecInDecl and ExecOutDecl are two classes with identical shape and no common base, so every
    // primitive below branches on _isEntry rather than the model being written twice. Six one-liners
    // beat a duplicated model, and beat inventing a base type in the serialised asset schema.

    private void Mutate(string label, Action apply, Action undo)
    {
        if (_record == null)
        {
            apply();
            _onChanged();
            return;
        }
        _record(label, () => { apply(); _onChanged(); }, () => { undo(); _onChanged(); });
    }

    private int IndexOfId(Guid id)
        => _isEntry ? _macro.ExecInputs.FindIndex(d => d.Id == id)
                    : _macro.ExecOutputs.FindIndex(d => d.Id == id);

    private int IndexOfName(string name)
        => _isEntry ? _macro.ExecInputs.FindIndex(d => d.Name == name)
                    : _macro.ExecOutputs.FindIndex(d => d.Name == name);

    private bool IsTaken(string name, Guid? exceptId)
        => Declarations.Any(d => d.Id != exceptId && string.Equals(d.Name, name, StringComparison.Ordinal));

    private void Insert(Guid id, string name, int index = -1)
    {
        if (IndexOfId(id) >= 0) return;
        if (_isEntry)
        {
            var d = new ExecInDecl { Id = id, Name = name };
            if (index >= 0 && index <= _macro.ExecInputs.Count) _macro.ExecInputs.Insert(index, d);
            else                                                _macro.ExecInputs.Add(d);
        }
        else
        {
            var d = new ExecOutDecl { Id = id, Name = name };
            if (index >= 0 && index <= _macro.ExecOutputs.Count) _macro.ExecOutputs.Insert(index, d);
            else                                                 _macro.ExecOutputs.Add(d);
        }
    }

    private void RemoveById(Guid id)
    {
        var i = IndexOfId(id);
        if (i < 0) return;
        if (_isEntry) _macro.ExecInputs.RemoveAt(i);
        else          _macro.ExecOutputs.RemoveAt(i);
    }

    private void SetName(Guid id, string name)
    {
        var i = IndexOfId(id);
        if (i < 0) return;
        if (_isEntry) _macro.ExecInputs[i].Name = name;
        else          _macro.ExecOutputs[i].Name = name;
    }

    private void MoveAt(int fromIndex, int toIndex)
    {
        if (_isEntry)
        {
            if (fromIndex >= _macro.ExecInputs.Count) return;
            var item = _macro.ExecInputs[fromIndex];
            _macro.ExecInputs.RemoveAt(fromIndex);
            _macro.ExecInputs.Insert(Math.Min(toIndex, _macro.ExecInputs.Count), item);
        }
        else
        {
            if (fromIndex >= _macro.ExecOutputs.Count) return;
            var item = _macro.ExecOutputs[fromIndex];
            _macro.ExecOutputs.RemoveAt(fromIndex);
            _macro.ExecOutputs.Insert(Math.Min(toIndex, _macro.ExecOutputs.Count), item);
        }
    }

    /// <summary>Whole-list snapshot — see the class docs on why a shallow copy is not enough.</summary>
    private List<ExecDeclView> Snapshot() => Declarations.ToList();

    private void RestoreFrom(List<ExecDeclView> snapshot)
    {
        if (_isEntry)
        {
            _macro.ExecInputs.Clear();
            foreach (var d in snapshot) _macro.ExecInputs.Add(new ExecInDecl { Id = d.Id, Name = d.Name });
        }
        else
        {
            _macro.ExecOutputs.Clear();
            foreach (var d in snapshot) _macro.ExecOutputs.Add(new ExecOutDecl { Id = d.Id, Name = d.Name });
        }
    }

    private string NounPlural => _isEntry ? "entries" : "exits";
    private string Noun       => _isEntry ? "entry"   : "exit";
    private string Label(string verb, string name) => $"{verb} macro {Noun} '{name}'";
    private string LabelRename(string a, string b)  => $"Rename macro {Noun} '{a}' → '{b}'";
}

/// <summary>Read-only projection of one exec declaration, for views and tests.</summary>
public readonly record struct ExecDeclView(Guid Id, string Name);
