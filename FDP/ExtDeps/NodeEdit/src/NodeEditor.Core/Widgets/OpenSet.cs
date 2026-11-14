namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ §10.1 (second sequence) — the per-ITEM open flags of a list, as one pure value: ImGui keeps state per index, so when an item moves / is inserted / is removed the flag must move with it.
/// <see cref="ListBlock"/> owns one per list and applies every op to it; the logic is here (no ImGui) so it is unit-tested.
/// </summary>
public sealed class OpenSet
{
    private bool[] _f = new bool[16];
    private int _n;

    /// <summary>How many flags are tracked (the highest index ever set + 1).</summary>
    public int Count => _n;

    public bool Get(int i) => (uint)i < (uint)_n && _f[i];

    public void Set(int i, bool open)
    {
        if (i < 0) return;
        Ensure(i + 1);
        _f[i] = open;
    }

    public void Toggle(int i) => Set(i, !Get(i));

    /// <summary>The item at <paramref name="from"/> now lives at <paramref name="to"/>; the items between shift by one — the open flag travels with its item.</summary>
    public void Move(int from, int to)
    {
        if (from < 0 || to < 0 || from == to) return;
        Ensure(Math.Max(from, to) + 1);
        bool v = _f[from];
        if (from < to) Array.Copy(_f, from + 1, _f, from, to - from);
        else Array.Copy(_f, to, _f, to + 1, from - to);
        _f[to] = v;
    }

    /// <summary>A new (closed) item at <paramref name="i"/>: every flag at or after it shifts up.</summary>
    public void Insert(int i)
    {
        if (i < 0) return;
        if (i >= _n) { Ensure(i + 1); _f[i] = false; return; }
        Ensure(_n + 1);
        Array.Copy(_f, i, _f, i + 1, _n - 1 - i);
        _f[i] = false;
    }

    /// <summary>The item at <paramref name="i"/> is gone: every flag after it shifts down.</summary>
    public void RemoveAt(int i)
    {
        if ((uint)i >= (uint)_n) return;
        Array.Copy(_f, i + 1, _f, i, _n - 1 - i);
        _f[--_n] = false;
    }

    public void Clear() { Array.Clear(_f, 0, _n); _n = 0; }

    private void Ensure(int n)
    {
        if (n > _f.Length) Array.Resize(ref _f, Math.Max(n, _f.Length * 2));
        if (n > _n) _n = n;
    }
}
