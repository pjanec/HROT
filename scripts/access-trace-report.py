#!/usr/bin/env python3
"""access-trace-report.py — turn the A-1 access tracer's JSON (HROT_ACCESS_TRACE=1, FDP/Engine/Fdp.Core/AccessTrace.cs) into the three tables of
docs/blueprints/batches/HANDOFF_Coordinator_To_Backend_Threaded_Analysis_Instruments.md A-1:

    1. the CONFLICT SET    — every world type the TICK writes and the PRESENTATION reads or writes (and the reverse)
    2. the PER-READER set  — per panel / layer / 3-D layer / gizmo rule / system: the types each reads and writes
    3. the DIFF vs §13     — what the tracer saw crossing the line that docs/DESIGN_Threaded_Simulation_Presentation.md §13 does not list

    python3 scripts/access-trace-report.py trace.json [--top 14] [--full per-reader-full.md] > tables.md

Rows of the JSON: [context, kind, reader, repo, phase, type, count]; for the Bus* kinds `repo` is a BUS id, otherwise a repository id.
⛔ A reader that was never ENTERED is an UNKNOWN, not "reads nothing" — section 4 lists them.
"""
import argparse
import collections
import json
import re
import sys

READS = {"Read", "ReadViaRW", "TryGetRW", "Has", "QueryInclude", "QueryExclude", "BusRead", "SingletonHas", "RawRead", "EntityIndexRead", "WorldCopy", "TableAccess"}
WRITES = {"Write", "StructAdd", "StructRemove", "SingletonSet", "BusPublish", "BusSwap", "EntityCreate", "EntityDestroy", "RawWrite"}
BUMP = {"TryGetRW", "ReadViaRW"}   # reads that take the RW path: they bump the table's chunk version
BUS = {"BusPublish", "BusRead", "BusSwap"}
SINGLETON = {"SingletonGet", "SingletonSet", "SingletonHas", "SingletonEnumerate"}
ENTITY = {"EntityCreate", "EntityDestroy"}

# §13.1 — the world singletons the UI reads (+ the UI-owned row), by short name
S13_SINGLETONS = {
    "TerrainWorld", "IGeographicTransform", "LiveTerrainBakeInfo", "ScenarioDescriptionResident", "TrafficNetCache", "INavmeshProvider",
    "ICoverProvider", "IEqsTemplateRegistry", "SpatialGridData", "GlobalTime", "ShotTraces", "DetonationTraces", "NetworkEntityMap",
    "ZoneEnvironmentData", "TrafficFrame", "ITkbDatabase", "IOrderForm", "OrdersOffered", "CanvasContextMenuState", "SortedHierarchyData",
}
# §13.2 — side tables are NOT world accesses (ConditionalWeakTable); the tracer cannot see them. §13.6 — objects outside the world: same.


def short(t):
    t = t.split("`")[0]
    return t.rsplit(".", 1)[-1] if "." in t and not t.startswith("(") else t


def domain(kind, typ):
    if kind in BUS:
        return "event"
    if kind in SINGLETON:
        return "singleton"
    if kind in ENTITY:
        return "entity"
    if typ.startswith("("):
        return "index/meta"
    return "component"


def merge(docs):
    """Several processes' traces as one: readers and phases are unified BY NAME; a repository / bus is the SAME one across processes when it was
    built by the same code in the same order (the legend's "built by …" text + its ordinal among equal texts)."""
    if len(docs) == 1:
        return docs[0]
    out = {"readers": [], "phases": [], "declared": set(), "repos": [], "buses": [], "kinds": docs[0]["kinds"], "contexts": docs[0]["contexts"], "rows": [], "entries": []}
    rid, pid = {}, {}
    canon_repo, canon_bus = {}, {}

    def canon(legend, table, label):
        out_list = out[label]
        ids = {}
        seen = collections.Counter()
        for i, txt in enumerate(legend):
            key = re.sub(r"^(r|bus)\d+: ", "", txt.split(" (context")[0].split(", created in")[0].split(" — ")[0])
            key = re.sub(r"\bworld r\d+'s", "world", key)
            seen[key] += 1
            k = (key, seen[key])
            if k not in table:
                table[k] = len(table) + 1
                out_list.append(f"{'r' if label == 'repos' else 'bus'}{table[k]}: {txt.split(': ', 1)[-1]}")
            ids[i + 1] = table[k]
        return ids

    for i, d in enumerate(docs):
        rmap = [rid.setdefault(n, len(rid)) for n in d["readers"]]
        pmap = [pid.setdefault(n, len(pid)) for n in d["phases"]]
        out["declared"].update(d["declared"])
        repo_ids = canon(d["repos"], canon_repo, "repos")
        bus_ids = canon(d["buses"], canon_bus, "buses")
        for c, k, r, repo, ph, t, n in d["rows"]:
            ids = bus_ids if d["kinds"][k] in BUS else repo_ids
            out["rows"].append([c, k, rmap[r], ids.get(repo, repo) if repo else 0, pmap[ph], t, n])
        for c, r, ph, n in d["entries"]:
            out["entries"].append([c, rmap[r], pmap[ph], n])
    out["readers"] = [n for n, _ in sorted(rid.items(), key=lambda x: x[1])]
    out["phases"] = [n for n, _ in sorted(pid.items(), key=lambda x: x[1])]
    out["declared"] = sorted(out["declared"])
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("json", nargs="+", help="one trace file per editor process; they are merged (repository / bus ids offset by 100 per file)")
    ap.add_argument("--top", type=int, default=14, help="types listed per reader in table 2 (the rest is a count)")
    ap.add_argument("--full", help="also write table 2 with EVERY type to this file")
    a = ap.parse_args()
    d = merge([json.load(open(f)) for f in a.json])
    readers, phases, ctxn, kinds = d["readers"], d["phases"], d["contexts"], d["kinds"]

    rows = []  # (ctx, kind, reader, repo, phase, type, n)
    for c, k, r, repo, ph, t, n in d["rows"]:
        rows.append((ctxn[c], kinds[k], readers[r], repo, phases[ph], t, n))
    entries = collections.Counter()
    entries_by_phase = collections.defaultdict(set)
    for c, r, ph, n in d["entries"]:
        entries[(ctxn[c], readers[r])] += n
        entries_by_phase[(ctxn[c], readers[r])].add(phases[ph])

    total = sum(r[6] for r in rows)
    out = []
    P = out.append
    P(f"**Trace:** {len(rows)} aggregate rows · {total:,} recorded accesses · {len(readers)} reader names · phases: {', '.join(phases)}")
    by_ctx = collections.Counter()
    for r in rows:
        by_ctx[r[0]] += r[6]
    P("**Accesses by context:** " + " · ".join(f"{k} {v:,}" for k, v in by_ctx.most_common()))
    P("")
    # headline numbers
    def types_of(ctx, kinds_, dom_):
        return {short(t) for c, k, rd, repo, ph, t, n in rows if c == ctx and k in kinds_ and domain(k, t) == dom_}
    comp_read = types_of("Presentation", READS - {"EntityIndexRead"}, "component")
    comp_write = types_of("Presentation", WRITES, "component")
    sing = types_of("Presentation", READS | WRITES | {"SingletonGet"}, "singleton")
    ev_r = types_of("Presentation", {"BusRead"}, "event"); ev_w = types_of("Presentation", {"BusPublish"}, "event")
    t_comp_w = types_of("Tick", WRITES, "component"); t_sing = types_of("Tick", READS | WRITES | {"SingletonGet"}, "singleton")
    typed = {short(t) for c, k, rd, repo, ph, t, n in rows if c == "Presentation" and k in {"Read", "Has", "QueryInclude", "QueryExclude"} and domain(k, t) == "component"}
    typed_no_insp = {short(t) for c, k, rd, repo, ph, t, n in rows if c == "Presentation" and k in {"Read", "Has", "QueryInclude", "QueryExclude"} and domain(k, t) == "component"
                     and rd != "window:editor_fdp_inspector"}
    P(f"**Headline:** the PRESENTATION touched {len(comp_read)} component types on the read side — {len(typed)} through typed reads / has / queries "
      f"({len(typed_no_insp)} without the FDP inspector window, which reads by reflection); the static X-2 count was 125 — and wrote {len(comp_write)}; "
      f"{len(sing)} singletons (X-2: 16); read {len(ev_r)} event types, published {len(ev_w)}. The TICK wrote {len(t_comp_w)} component types and touched {len(t_sing)} singletons.")
    P("")
    P("Repositories: " + "; ".join(d["repos"][:12]) + (f" … (+{len(d['repos']) - 12})" if len(d["repos"]) > 12 else ""))
    P("")
    P("Buses: " + "; ".join(d["buses"][:12]) + (f" … (+{len(d['buses']) - 12})" if len(d["buses"]) > 12 else ""))
    P("")

    # ---------------------------------------------------------------- the (none) bucket
    none = collections.Counter()
    none_types = collections.defaultdict(collections.Counter)
    for c, k, rd, repo, ph, t, n in rows:
        if rd == "(none)":
            none[c] += n
            none_types[c][(k, short(t))] += n
    P("## 0. The untagged bucket — reader `(none)`")
    P("")
    P("| context | untagged accesses | of the context's total | top (kind · type) |")
    P("|---|---|---|---|")
    for c in ("Tick", "Presentation", "Other"):
        tot = by_ctx.get(c, 0)
        if tot == 0:
            continue
        top = ", ".join(f"{k} {t} ({n:,})" for (k, t), n in none_types[c].most_common(5))
        P(f"| {c} | {none[c]:,} | {100.0 * none[c] / tot:.1f} % | {top or '—'} |")
    P("")

    # ---------------------------------------------------------------- aggregate per (domain, type, repo, ctx)
    # side[(dom, type, repo)] -> {"TickW": Counter(reader), "TickR", "TickG", "PresW", "PresR", "PresG", "OtherW", "OtherR"}
    side = collections.defaultdict(lambda: collections.defaultdict(collections.Counter))
    for c, k, rd, repo, ph, t, n in rows:
        if k in ("EntityIndexRead",) or c == "Other" and False:
            pass
        dom = domain(k, t)
        key = (dom, t, repo)
        pre = {"Tick": "Tick", "Presentation": "Pres", "Other": "Other"}[c]
        if k in WRITES:
            side[key][pre + "W"][rd] += n
        elif k == "SingletonGet":
            side[key][pre + "G"][rd] += n
        else:
            side[key][pre + "R"][rd] += n

    def top(cnt, n=3):
        return ", ".join(f"{r} ({v:,})" for r, v in cnt.most_common(n)) if cnt else "—"

    P("## 1. The conflict set — what the TICK writes that the PRESENTATION touches, and the reverse")
    P("")
    P("Same repository (or bus) on both sides. `get-by-ref` = a singleton fetched by reference (read or write — the tracer cannot tell); `TryGetRW` is a TryGetComponent (it takes the RW path and bumps the chunk version).")
    P("")
    conflicts = []
    for (dom, t, repo), s in side.items():
        tw, tr, tg, pw, pr, pg = s["TickW"], s["TickR"], s["TickG"], s["PresW"], s["PresR"], s["PresG"]
        kinds_found = []
        pres_touch = bool(pr or pw or pg)
        if tw and pres_touch:
            kinds_found.append("Tick writes → Pres " + "/".join(x for x, v in (("reads", pr), ("writes", pw), ("get-by-ref", pg)) if v))
        if pw and (tr or tg):
            kinds_found.append("Pres writes → Tick " + "/".join(x for x, v in (("reads", tr), ("get-by-ref", tg)) if v))
        if dom == "singleton" and not tw and not pw and tg and pg:
            kinds_found.append("get-by-ref on BOTH sides (write unproven)")
        if dom == "singleton" and not tw and not pw and tg and pr and not pg:
            kinds_found.append("Tick get-by-ref → Pres has/read (write unproven)")
        if kinds_found:
            conflicts.append((dom, t, repo, kinds_found, s))
    conflicts.sort(key=lambda x: (x[0], -sum(x[4]["TickW"].values()) - sum(x[4]["PresW"].values())))
    P(f"**{len(conflicts)} types / singletons / event types cross the line** "
      f"({collections.Counter(c[0] for c in conflicts)}).")
    P("")
    P("| domain | type | repo/bus | direction | Tick writers (top 3) | Presentation readers (top 3) | Presentation writers (top 3) | Tick readers (top 3) |")
    P("|---|---|---|---|---|---|---|---|")
    for dom, t, repo, kf, s in conflicts:
        pres_r = s["PresR"] + s["PresG"]
        tick_r = s["TickR"] + s["TickG"]
        P(f"| {dom} | `{short(t)}` | {repo} | {'; '.join(kf)} | {top(s['TickW'])} | {top(pres_r)} | {top(s['PresW'])} | {top(tick_r)} |")
    P("")
    # reads that bump a chunk version
    bump = collections.defaultdict(lambda: collections.defaultdict(collections.Counter))
    for c, k, rd, repo, ph, t, n in rows:
        if k in BUMP:
            bump[(k, short(t))][c][rd] += n
    P("### 1b. Reads that take the RW path — they bump the table's chunk version, so a READ is a write to shared state")
    P("")
    P("`ReadViaRW` = a managed `GetComponentRO` / `GetComponent` (`UnsafeShim.ManagedAccessor.GetRO` returns the RW reference); `TryGetRW` = `TryGetComponent` (calls `GetComponentRW`). "
      "A reader on one thread dirtying a chunk version another thread reads or the recorder consumes is a cross-thread write even though no value changes.")
    P("")
    P("| kind | type | Tick (readers, top 2) | Presentation (readers, top 3) | Other |")
    P("|---|---|---|---|---|")
    for (k, t), per_ctx in sorted(bump.items(), key=lambda x: -sum(sum(c.values()) for c in x[1].values()))[:60]:
        if not per_ctx.get("Presentation"):
            continue
        P(f"| {k} | `{t}` | {sum(per_ctx['Tick'].values()):,} · {top(per_ctx['Tick'], 2)} | {sum(per_ctx['Presentation'].values()):,} · {top(per_ctx['Presentation'])} | {sum(per_ctx['Other'].values()):,} |")
    P("")
    tick_only = sorted({t for (k, t), per_ctx in bump.items() if per_ctx.get("Tick") and not per_ctx.get("Presentation")})
    P(f"Types the TICK reads through the RW path and the presentation never does: {len(tick_only)} (e.g. {', '.join('`' + x + '`' for x in tick_only[:12])}).")
    P("")

    # other-context writers (HTTP threads, pool threads) of things the tick also touches
    oth = [(dom, t, repo, s) for (dom, t, repo), s in side.items() if s["OtherW"] and (s["TickW"] or s["TickR"] or s["TickG"])]
    if oth:
        P(f"**Untagged threads (context Other: HTTP API, pool, startup) that WRITE something the tick touches — {len(oth)}:** "
          + ", ".join(f"`{short(t)}`" for dom, t, repo, s in sorted(oth, key=lambda x: -sum(x[3]['OtherW'].values()))[:25]) + (" …" if len(oth) > 25 else ""))
        P("")

    # ---------------------------------------------------------------- table 2
    P("## 2. The per-reader sets — what each presentation reader touches (= its mask)")
    P("")
    P("Components read = `Read`, `Has`, query inclusion, raw reads, table access; singletons = get-by-ref / has / set; events = bus reads; W = anything written. "
      "Counts are accesses over the whole run; `frames` = how many times the reader's scope was entered.")
    P("")
    per = collections.defaultdict(lambda: {"comp": collections.Counter(), "single": collections.Counter(), "event": collections.Counter(),
                                          "write": collections.Counter(), "idx": collections.Counter()})
    for c, k, rd, repo, ph, t, n in rows:
        dom = domain(k, t)
        key = (c, rd)
        if k in WRITES:
            per[key]["write"][f"{short(t)}·{k}"] += n
        elif dom == "component":
            per[key]["comp"][short(t)] += n
        elif dom == "singleton":
            per[key]["single"][short(t) + ("" if k != "SingletonHas" else "?")] += n
        elif dom == "event":
            per[key]["event"][short(t)] += n
        else:
            per[key]["idx"][short(t)] += n

    def fmt(cnt, limit):
        if not cnt:
            return "—"
        items = [f"{t}" for t, n in cnt.most_common(limit)]
        more = len(cnt) - len(items)
        return ", ".join(items) + (f" (+{more})" if more > 0 else "") + f" [{len(cnt)}]"

    def groups(ctx):
        g = collections.defaultdict(list)
        for (c, rd), v in per.items():
            if c != ctx:
                continue
            prefix = rd.split(":", 1)[0] if ":" in rd else "(untagged)"
            g[prefix].append((rd, v))
        return g

    full = [] if a.full else None
    for ctx in ("Presentation", "Tick"):
        g = groups(ctx)
        P(f"### 2{'a' if ctx == 'Presentation' else 'b'}. {ctx} readers ({sum(len(v) for v in g.values())} entered)")
        P("")
        P("| reader | frames | components read | singletons | events read | writes |")
        P("|---|---|---|---|---|---|")
        for prefix in sorted(g, key=lambda p: -sum(sum(v["comp"].values()) for _, v in g[p])):
            for rd, v in sorted(g[prefix], key=lambda x: -sum(x[1]["comp"].values())):
                fr = entries.get((ctx, rd), 0)
                P(f"| `{rd}` | {fr:,} | {fmt(v['comp'], a.top)} | {fmt(v['single'], 8)} | {fmt(v['event'], 6)} | {fmt(v['write'], 6)} |")
                if full is not None:
                    full.append(f"### {ctx} · `{rd}` (frames {fr})\n- components: {', '.join(f'{t} {n}' for t, n in v['comp'].most_common())}\n"
                                f"- singletons: {', '.join(f'{t} {n}' for t, n in v['single'].most_common())}\n"
                                f"- events: {', '.join(f'{t} {n}' for t, n in v['event'].most_common())}\n"
                                f"- writes: {', '.join(f'{t} {n}' for t, n in v['write'].most_common())}\n"
                                f"- index/meta: {', '.join(f'{t} {n}' for t, n in v['idx'].most_common())}\n")
        P("")
    if full is not None:
        open(a.full, "w").write("# per-reader sets, complete\n\n" + "\n".join(full))

    # ---------------------------------------------------------------- 2c: what a selection adds
    def phase_sets(pred):
        out_ = collections.defaultdict(set)
        for c, k, rd, repo, ph, t, n in rows:
            if c == "Presentation" and pred(ph) and k not in WRITES and domain(k, t) in ("component", "singleton"):
                out_[rd].add(short(t))
        return out_
    idle = phase_sets(lambda ph: ph.startswith("edit-idle"))
    sel = phase_sets(lambda ph: ph.startswith("select:") or ph.startswith("focus:"))
    P("### 2c. What a SELECTION adds — types a presentation reader read in the `select:` phases that it did NOT read in `edit-idle` (nothing selected)")
    P("")
    P("| reader | new types read once a unit is selected |")
    P("|---|---|")
    anyrow = False
    for rd in sorted(sel, key=lambda r: -len(sel[r] - idle.get(r, set()))):
        new = sel[rd] - idle.get(rd, set())
        if new:
            anyrow = True
            P(f"| `{rd}` | {', '.join(sorted(new)[:16])}{f' (+{len(new) - 16})' if len(new) > 16 else ''} [{len(new)}] |")
    if not anyrow:
        P("| — | nothing new (or the selection phases were not run) |")
    P("")

    # ---------------------------------------------------------------- table 3
    P("## 3. The diff against §13")
    P("")
    pres_single = set(); tick_single = set(); cross_single = set()
    for (dom, t, repo), s in side.items():
        if dom != "singleton":
            continue
        p = bool(s["PresR"] or s["PresG"] or s["PresW"]); k_ = bool(s["TickR"] or s["TickG"] or s["TickW"])
        if p: pres_single.add(short(t))
        if k_: tick_single.add(short(t))
        if p and k_: cross_single.add(short(t))
    found_cross_unlisted = sorted(cross_single - S13_SINGLETONS)
    found_pres_only_unlisted = sorted((pres_single - tick_single) - S13_SINGLETONS)
    listed_not_seen = sorted(S13_SINGLETONS - pres_single)
    listed_seen_not_crossing = sorted((S13_SINGLETONS & pres_single) - cross_single)
    P("| check | result |")
    P("|---|---|")
    P(f"| singletons touched by BOTH contexts, ⭐ NOT in §13.1 — **findings** ({len(found_cross_unlisted)}) | {', '.join(f'`{x}`' for x in found_cross_unlisted) or '—'} |")
    P(f"| singletons the presentation touches that the tick never does, not in §13.1 ({len(found_pres_only_unlisted)}) | {', '.join(f'`{x}`' for x in found_pres_only_unlisted) or '—'} |")
    P(f"| §13.1 singletons crossing, confirmed ({len(S13_SINGLETONS & cross_single)}) | {', '.join(f'`{x}`' for x in sorted(S13_SINGLETONS & cross_single)) or '—'} |")
    P(f"| §13.1 singletons the presentation touched but the tick did not in this run ({len(listed_seen_not_crossing)}) | {', '.join(f'`{x}`' for x in listed_seen_not_crossing) or '—'} |")
    P(f"| §13.1 singletons NOT seen from the presentation at all ({len(listed_not_seen)}) — unexercised, or the reader goes through a helper the tracer does not see | {', '.join(f'`{x}`' for x in listed_not_seen) or '—'} |")
    ev = sorted({short(t) for (dom, t, repo), s in side.items() if dom == "event" and ((s['TickW'] and (s['PresR'])) or (s['PresW'] and s['TickR']))})
    ev_p = sorted({short(t) for (dom, t, repo), s in side.items() if dom == "event" and s['PresW']})
    P(f"| event types published by the presentation ({len(ev_p)}); §13.6 names only \"the buses\" as a row, no event types | {', '.join(f'`{x}`' for x in ev_p[:60]) or '—'}{' …' if len(ev_p) > 60 else ''} |")
    P(f"| event types that cross (one side publishes, the other reads) ({len(ev)}) | {', '.join(f'`{x}`' for x in ev[:60]) or '—'}{' …' if len(ev) > 60 else ''} |")
    comp_cross = [(t, repo) for (dom, t, repo), s in side.items() if dom == "component" and ((s['TickW'] and (s['PresR'] or s['PresW'])) or (s['PresW'] and s['TickR']))]
    comp_pres_w = sorted({short(t) for (dom, t, repo), s in side.items() if dom == "component" and s['PresW']})
    P(f"| component types the Tick writes and the presentation reads / writes, or the reverse — §13 treats components by the §12 mask, lists none ({len({short(t) for t, _ in comp_cross})}) | see table 1 |")
    P(f"| ⭐ component types the PRESENTATION WRITES ({len(comp_pres_w)}) — direct world writes by the UI (§13 names ~15 sites in X-2) | {', '.join(f'`{x}`' for x in comp_pres_w[:80]) or '—'}{' …' if len(comp_pres_w) > 80 else ''} |")
    P("")
    P("§13.2 (the 44 `ConditionalWeakTable<EntityRepository,…>` side tables) and §13.6 (objects outside the world: `ClusterUiCache`, `MasterSyncController`, `DebugPrimitiveBuffer`, "
      "`BehaviorRegistry`, `TkbDatabase` …) are NOT world accesses — ⛔ this tracer cannot see them; A-2 (the static analysis) owns them.")
    P("")

    # ---------------------------------------------------------------- unreached
    P("## 4. What the run did not reach — UNKNOWNS, never \"reads nothing\"")
    P("")
    entered = {rd for (c, rd) in entries}
    declared = sorted(x for x in d["declared"] if x not in entered)
    P(f"**Windows the editor offers (declared by the window manager) that were never open during the run — {len(declared)} of {len(d['declared'])}:**")
    P("")
    P(", ".join(f"`{x.split(':', 1)[1] if ':' in x else x}`" for x in declared) or "—")
    P("")
    P("**Presentation readers by the phase they were FIRST entered in** (windows stay open once opened, so later phases re-enter them):")
    P("")
    P("| phase | newly entered readers |")
    P("|---|---|")
    first = {}
    order = {ph: i for i, ph in enumerate(phases)}
    for (c, rd), phs in entries_by_phase.items():
        if c == "Presentation":
            first[rd] = min(phs, key=lambda x: order[x])
    byfirst = collections.defaultdict(list)
    for rd, ph in first.items():
        byfirst[ph].append(rd)
    for ph in phases:
        if byfirst.get(ph):
            P(f"| {ph} | {', '.join('`' + x + '`' for x in sorted(byfirst[ph]))} |")
    P("")
    print("\n".join(out))


if __name__ == "__main__":
    main()
