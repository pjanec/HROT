// CE-4454 R-1 — the per-read cross-entity check, micro-benchmark (Release, Stopwatch, 0 B per access).
// One process per case ROW so the static readonly switch is folded exactly as in production:
//   dotnet <dll> today            (HROT_READ_CHECK unset)            ① today
//   HROT_READ_CHECK=1 dotnet <dll> off     ② compiled in, phase flag OFF (serial code)
//   HROT_READ_CHECK=1 dotnet <dll> own     ③ flag ON, the processed entity's own components
//   HROT_READ_CHECK=1 dotnet <dll> other   ④ flag ON, ANOTHER entity — served from the last-tick copy
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Fdp.Core;

[ComponentId(240)] public struct Pos { public float X, Y, Z, W; }
[ComponentId(241)] public sealed class Tag { public int V; }
[ComponentId(242)] public struct Undeclared { public int V; }

public static class Program
{
    const int N = 4096;            // entities (fits L2 like a busy chunk walk)
    const int Reps = 4000;         // passes ⇒ 16.4 M accesses per measurement
    const int Rounds = 7;          // median of 7

    public static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "today";
        if (mode == "check") return Check();
        var repo = new EntityRepository();
        repo.RegisterComponent<Pos>();
        repo.RegisterManagedComponent<Tag>();
        var es = new Entity[N];
        for (int i = 0; i < N; i++)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Pos { X = i, Y = 1, Z = 2, W = 3 });
            repo.SetManagedComponent(e, new Tag { V = i });
            es[i] = e;
        }

        bool active = mode is "own" or "other";
        bool other = mode == "other";
        if (active)
        {
            ParallelReadCheck.DeclareUnmanaged<Pos>();
            ParallelReadCheck.DeclareManaged<Tag>();
            ParallelReadCheck.CopyUnmanaged<Pos>(repo);
            ParallelReadCheck.CopyManaged<Tag>(repo);
            ParallelReadCheck.Active = true;
        }
        Console.WriteLine($"mode={mode} compiled={ParallelReadCheck.Compiled} active={ParallelReadCheck.Active}");

        Report("GetComponentRO<Pos>", () => RO(repo, es, other));
        if (!other) Report("GetComponentRW<Pos>", () => RW(repo, es));
        else Console.WriteLine("GetComponentRW<Pos>                n/a — a cross-entity write is a command (§18), not a read");
        Report("TryGetComponent<Pos>", () => TryGet(repo, es, other));
        Report("GetComponentRO<Tag> (managed)", () => Managed(repo, es, other));
        return 0;
    }

    // ⭐ the rail: what the prototype claims, asserted (HROT_READ_CHECK=1 dotnet <dll> check)
    static int Check()
    {
        if (!ParallelReadCheck.Compiled) { Console.WriteLine("check: run with HROT_READ_CHECK=1"); return 2; }
        var repo = new EntityRepository();
        repo.RegisterComponent<Pos>(); repo.RegisterComponent<Undeclared>(); repo.RegisterManagedComponent<Tag>();
        var a = repo.CreateEntity(); var b = repo.CreateEntity();
        foreach (var e in new[] { a, b }) { repo.AddComponent(e, new Pos { X = 1 }); repo.AddComponent(e, new Undeclared { V = 1 }); repo.SetManagedComponent(e, new Tag { V = 1 }); }
        ParallelReadCheck.DeclareUnmanaged<Pos>(); ParallelReadCheck.DeclareManaged<Tag>();
        ParallelReadCheck.CopyUnmanaged<Pos>(repo); ParallelReadCheck.CopyManaged<Tag>(repo);
        repo.GetComponentRW<Pos>(b).X = 2; repo.SetManagedComponent(b, new Tag { V = 2 });     // this tick's write, after the copy
        int fails = 0;
        void Expect(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
        bool Throws(Action f) { try { f(); return false; } catch (InvalidOperationException) { return true; } }
        ParallelReadCheck.Active = true; ParallelReadCheck.Current = a.Index;
        Expect(repo.GetComponentRO<Pos>(b).X == 1, "a cross-entity read is LAST tick's value (copy 1, live 2)");
        Expect(repo.TryGetComponent<Pos>(b, out var p) && p.X == 1, "TryGetComponent across entities: last tick too");
        Expect(repo.GetComponentRO<Tag>(b).V == 1, "a managed cross-entity read: last tick's reference");
        Expect(repo.GetComponentRO<Pos>(a).X == 1 && Throws(() => repo.GetComponentRO<Undeclared>(b)), "own reads live; an UNDECLARED cross-entity read throws");
        Expect(Throws(() => repo.GetComponentRW<Pos>(b)), "a cross-entity write throws (a command in the design)");
        ParallelReadCheck.Current = b.Index;
        Expect(repo.GetComponentRO<Pos>(b).X == 2, "the processed entity reads its own LIVE value");
        ParallelReadCheck.Active = false;
        Expect(repo.GetComponentRO<Pos>(b).X == 2 && repo.GetComponentRO<Undeclared>(b).V == 1, "phase off: every read is live, nothing throws");
        Console.WriteLine(fails == 0 ? "check: all pass" : $"check: {fails} FAILED");
        return fails == 0 ? 0 : 1;
    }

    static void Report(string name, Func<float> run)
    {
        run(); run();   // JIT to tier 1
        var ns = new double[Rounds];
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        float sink = 0;
        for (int r = 0; r < Rounds; r++)
        {
            var sw = Stopwatch.StartNew();
            sink += run();
            ns[r] = sw.Elapsed.TotalMilliseconds * 1e6 / ((double)N * Reps);
        }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        Array.Sort(ns);
        Console.WriteLine($"{name,-34} {ns[Rounds / 2],6:F2} ns/access   (min {ns[0]:F2}, max {ns[^1]:F2}; {alloc} B over {Rounds} rounds; sink {sink:E1})");
    }

    // the walk: per entity set the current entity (what a per-entity job does), then read own or the NEXT entity
    [MethodImpl(MethodImplOptions.NoInlining)]
    static float RO(EntityRepository repo, Entity[] es, bool other)
    {
        float s = 0;
        for (int r = 0; r < Reps; r++)
            for (int i = 0; i < es.Length; i++)
            {
                if (ParallelReadCheck.Compiled && ParallelReadCheck.Active) ParallelReadCheck.Current = i;   // the walk sets it only during a phase
                var e = other ? es[(i + 1) & (N - 1)] : es[i];
                s += repo.GetComponentRO<Pos>(e).X;
            }
        return s;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static float RW(EntityRepository repo, Entity[] es)
    {
        float s = 0;
        for (int r = 0; r < Reps; r++)
            for (int i = 0; i < es.Length; i++)
            {
                if (ParallelReadCheck.Compiled && ParallelReadCheck.Active) ParallelReadCheck.Current = i;   // the walk sets it only during a phase
                ref var p = ref repo.GetComponentRW<Pos>(es[i]);
                p.W += 1; s += p.X;
            }
        return s;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static float TryGet(EntityRepository repo, Entity[] es, bool other)
    {
        float s = 0;
        for (int r = 0; r < Reps; r++)
            for (int i = 0; i < es.Length; i++)
            {
                if (ParallelReadCheck.Compiled && ParallelReadCheck.Active) ParallelReadCheck.Current = i;   // the walk sets it only during a phase
                var e = other ? es[(i + 1) & (N - 1)] : es[i];
                if (repo.TryGetComponent<Pos>(e, out var p)) s += p.X;
            }
        return s;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static float Managed(EntityRepository repo, Entity[] es, bool other)
    {
        float s = 0;
        for (int r = 0; r < Reps; r++)
            for (int i = 0; i < es.Length; i++)
            {
                if (ParallelReadCheck.Compiled && ParallelReadCheck.Active) ParallelReadCheck.Current = i;   // the walk sets it only during a phase
                var e = other ? es[(i + 1) & (N - 1)] : es[i];
                s += repo.GetComponentRO<Tag>(e).V;
            }
        return s;
    }
}
