"""hrot_ground_compare.py - CE-4651 (U-19h): does the Unreal level's ground match HROT's? (the user, 2026-11-23: on Stechovice soldiers float or
sink - "terrain heights differ between HROT and Unreal")

    UnrealEditor.exe <project> -ExecutePythonScript=<this> -unattended -nosplash
        HROT_GROUND_JOB = { "level": "/Game/Maps/Stechovice_look", "points": <json [{id, name, pos:[x, y, z]}] - HROT positions of people
                            standing on HROT's ground (GET /entities/{id} SimTransform)>, "out": <result json>, "searchM": 60, "stepM": 5 }

For each point: Unreal's ground straight below / above it (a visibility trace on the loaded level, the client's mapping (x, -y, z) * 100,
origin 0) and the difference HROT z - Unreal z. Then one horizontal shift (dx, dy) over a grid of +-searchM is searched for the smallest mean
absolute difference: a constant shift that removes the error means the two terrains are offset; none means they differ in shape. Facts only.
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_GROUND_JOB"], encoding="utf-8-sig"))
R = {"log": []}


def ground(world, x_cm, y_cm):
    a, b = unreal.Vector(x_cm, y_cm, 200000.0), unreal.Vector(x_cm, y_cm, -200000.0)
    hit = unreal.SystemLibrary.line_trace_single(world, a, b, unreal.TraceTypeQuery.TRACE_TYPE_QUERY1, False, [],
                                                 unreal.DrawDebugTrace.NONE, True)
    if hit is None: return None
    t = hit.to_tuple()
    return t[4].z if t[0] else None   # (blocking hit, initial overlap, time, distance, location, ...)


def main():
    les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not les.load_level(JOB["level"]): raise RuntimeError("load_level failed")
    world = unreal.EditorLevelLibrary.get_editor_world()
    pts = json.load(open(JOB["points"], encoding="utf-8"))
    rows = []
    for p in pts:
        x, y, z = p["pos"]
        g = ground(world, x * 100.0, -y * 100.0)
        rows.append({"id": p["id"], "name": p["name"], "hrotZ": round(z, 2), "unrealZ": None if g is None else round(g / 100.0, 2),
                     "diff": None if g is None else round(z - g / 100.0, 2)})
    R["points"] = rows
    d = [abs(r["diff"]) for r in rows if r["diff"] is not None]
    R["meanAbsDiff"] = round(sum(d) / max(1, len(d)), 2); R["maxAbsDiff"] = round(max(d) if d else 0, 2); R["n"] = len(d)
    # one constant horizontal shift?
    S, st = float(JOB.get("searchM", 60)), float(JOB.get("stepM", 5))
    best = None
    k = int(S / st)
    for i in range(-k, k + 1):
        for j in range(-k, k + 1):
            dx, dy = i * st, j * st
            e = []
            for p in pts:
                x, y, z = p["pos"]
                g = ground(world, (x + dx) * 100.0, -(y + dy) * 100.0)
                if g is not None: e.append(abs(z - g / 100.0))
            if e:
                m = sum(e) / len(e)
                if best is None or m < best[0]: best = (m, dx, dy, max(e))
    R["bestShift"] = {"meanAbsDiff": round(best[0], 2), "dxM": best[1], "dyM": best[2], "maxAbsDiff": round(best[3], 2)} if best else None


try:
    main()
except Exception:
    R["log"].append(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
unreal.SystemLibrary.quit_editor()
