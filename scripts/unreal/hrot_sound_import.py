"""hrot_sound_import.py - runs in the Unreal editor COMMANDLET (CE-4651, U-18n): imports the IG's cut sounds as sound waves.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi     HROT_SOUND_JOB = { "root": <cut root>, "dest": "/Game/Hrot/Audio",
                                                                                              "out": <result json> }
Every WAV under root/<group>/<name>.wav becomes /Game/Hrot/Audio/<group>/<name> (a USoundWave). The loops (the beds, the engines, the
trot, the fire, the flame; U-19d: amb/war_*, al_*, nv_* even as _NN variants) are marked looping; the one-shots (<name>_NN, amb/ev_*) are not. The raw library and the cut WAVs stay out of git.
"""
import json, os, re, traceback
import unreal

JOB = json.load(open(os.environ["HROT_SOUND_JOB"], encoding="utf-8-sig"))   # PowerShell writes a BOM
R = {"log": [], "assets": [], "loops": 0, "shots": 0}
LOOP_GROUPS = ("amb", "veh")
LOOP_NAMES = ("horse_trot", "horse_walk", "fire_crackle", "flame_roar")


def log(m):
    unreal.log_warning(f"[HROT-SOUND] {m}"); R["log"].append(m)


# U-19d: the war ambience layers (the purchased pack): beds, alarms and naval beds LOOP even as variants (war_aftermath_01 ..); the events (ev_*) never do
WAR_LOOPS = ("war_", "al_", "nv_")


def is_loop(group, name):
    if group == "amb" and name.startswith(WAR_LOOPS): return True
    if group == "amb" and name.startswith("ev_"): return False
    if re.search(r"_\d\d$", name): return False          # a one-shot variant
    return group in LOOP_GROUPS or name in LOOP_NAMES


def main():
    root, dest = JOB["root"], JOB.get("dest", "/Game/Hrot/Audio")
    tasks, meta = [], []
    for group in sorted(os.listdir(root)):
        d = os.path.join(root, group)
        if not os.path.isdir(d): continue
        for f in sorted(os.listdir(d)):
            if not f.lower().endswith(".wav"): continue
            name = f[:-4]
            t = unreal.AssetImportTask()
            t.set_editor_property("filename", os.path.join(d, f)); t.set_editor_property("destination_path", f"{dest}/{group}")
            t.set_editor_property("destination_name", name); t.set_editor_property("automated", True)
            t.set_editor_property("replace_existing", True); t.set_editor_property("save", False)
            tasks.append(t); meta.append((group, name))
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks(tasks)
    for (group, name) in meta:
        path = f"{dest}/{group}/{name}"
        w = unreal.load_asset(path)
        if not w: log(f"NOT IMPORTED {path}"); continue
        loop = is_loop(group, name)
        w.set_editor_property("looping", loop)
        unreal.EditorAssetLibrary.save_asset(path, only_if_is_dirty=False)
        R["assets"].append({"path": path, "loop": loop, "seconds": round(float(w.get_editor_property("duration")), 2)})
        R["loops" if loop else "shots"] += 1
    log(f"imported {len(R['assets'])} of {len(tasks)}: {R['loops']} loops, {R['shots']} one-shots")


try:
    main()
except Exception:
    R["error"] = traceback.format_exc(); log(R["error"])
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
