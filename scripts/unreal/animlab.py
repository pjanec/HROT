#!/usr/bin/env python3
"""animlab.py — CE-4651 (U-16): the animation contact sheet (scripts/unreal/hrot_animlab.py) for the user's list of human animations.

    python scripts/unreal/animlab.py --project D:/Work/HrotUnreal/HrotUnreal.uproject --shots <folder>

Each group is a row of mannequins; each entry a name pattern (regex, full match) resolved in the project's /Game/Characters (Lyra's
mannequin animations today). The result json lists what was found and what is MISSING per group.
"""
import argparse, json, os, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from look import free_gb

GROUPS = [
    {"name": "stand", "anims": ["MM_Unarmed_Idle_Ready", "MM_Unarmed_IdleBreak_Fidget", "MM_Rifle_Idle_Hipfire", "MM_Rifle_Idle_ADS",
                                "MM_Rifle_IdleBreak_Scan", "MM_Pistol_Idle_Hipfire", "MM_Pistol_Idle_ADS"]},
    {"name": "crouch", "anims": ["MM_Unarmed_Crouch_Idle", "MM_Rifle_Crouch_Idle", "MM_Pistol_Crouch_Idle", "MM_Unarmed_Crouch_Walk_Fwd",
                                 "MM_Rifle_Crouch_Walk_Fwd", "MM_Pistol_Crouch_Walk_Fwd"]},
    {"name": "move", "anims": ["MM_Unarmed_Walk_Fwd", "MM_Unarmed_Jog_Fwd", "MF_Rifle_Walk_Fwd", "MF_Rifle_Jog_Fwd", "MM_Pistol_Walk_Fwd",
                               "MM_Pistol_Jog_Fwd"]},
    {"name": "actions", "anims": ["MM_Rifle_Fire", "MM_Pistol_Fire", "MM_Rifle_GrenadeToss", "MM_Rifle_Reload", "MM_Rifle_Equip",
                                  "MM_Pistol_Equip", "MM_Rifle_Melee"]},
    {"name": "hits", "anims": ["MM_HitReact_Front_Hvy_01", "MM_HitReact_Left_Med_01", "MM_Death_Front_01", "MM_Death_Back_01"]},
    # ⭐ U-16 step 1: the STATE-DRIVEN person (UHrotPersonAnimInstance): base = stance + gait, upper = weapon + state, action = additive
    {"name": "layers", "anims": [
        {"label": "stand rifle lowered", "base": "MM_Rifle_Idle_Hipfire", "weapon": "rifle"},
        {"label": "stand rifle aimed", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle"},
        {"label": "walk rifle aimed", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle"},
        {"label": "jog rifle aimed", "base": "MF_Rifle_Jog_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle"},
        {"label": "crouch-walk rifle aimed", "base": "MM_Rifle_Crouch_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle"},
        {"label": "walk pistol aimed", "base": "MM_Pistol_Walk_Fwd", "upper": "MM_Pistol_Idle_ADS", "weapon": "pistol"},
        {"label": "walk unarmed", "base": "MM_Unarmed_Walk_Fwd"}]},
    {"name": "actions-aim", "anims": [
        {"label": "aimed + fire", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Fire", "weapon": "rifle"},
        {"label": "jog + reload", "base": "MF_Rifle_Jog_Fwd", "upper": "MM_Rifle_Idle_Hipfire", "action": "MM_Rifle_Reload_Additive", "weapon": "rifle"},
        {"label": "walk + grenade toss", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_Hipfire", "action": "MM_Rifle_GrenadeToss_Additive", "weapon": "rifle"},
        {"label": "aim yaw +45", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "yaw": 45, "weapon": "rifle"},
        {"label": "aim yaw -45", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "yaw": -45, "weapon": "rifle"},
        {"label": "aim pitch +25", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "pitch": 25, "weapon": "rifle"},
        {"label": "aim pitch -25", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "pitch": -25, "weapon": "rifle"}]},
    # the aim pitch seen from the SIDE (facing 90: the barrel crosses the picture) — to judge the angle, not foreshortened
    {"name": "pitch-side", "anims": [
        {"label": "pitch 0", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "pitch": 0, "facing": 90, "weapon": "rifle"},
        {"label": "pitch +10", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "pitch": 10, "facing": 90, "weapon": "rifle"},
        {"label": "pitch +25", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "pitch": 25, "facing": 90, "weapon": "rifle"},
        {"label": "pitch -25", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "pitch": -25, "facing": 90, "weapon": "rifle"}]},
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", required=True); ap.add_argument("--shots", required=True)
    ap.add_argument("--engine", default=r"C:\UE_5.8\UE_5.8"); ap.add_argument("--min-free-gb", type=float, default=6.0)
    a = ap.parse_args()
    os.makedirs(a.shots, exist_ok=True)
    fwd = lambda p: os.path.abspath(p).replace("\\", "/")
    out = os.path.join(a.shots, "animlab-result.json")
    job = {"groups": GROUPS, "shots": fwd(a.shots), "out": fwd(out), "settleSeconds": 20}
    jp = os.path.join(a.shots, "animlab-job.json"); json.dump(job, open(jp, "w"), indent=1)
    if os.path.exists(out): os.remove(out)
    umap = os.path.join(os.path.dirname(os.path.abspath(a.project)), "Content", "Maps", "AnimLab.umap")
    if os.path.exists(umap): os.remove(umap)
    exe = os.path.join(a.engine, "Engine", "Binaries", "Win64", "UnrealEditor.exe")
    t = time.time()
    proc = subprocess.Popen([exe, a.project, f"-ExecutePythonScript={os.path.join(HERE, 'hrot_animlab.py')}", "-unattended", "-nosplash",
                             "-NoSound", f"-abslog={os.path.join(a.shots, 'animlab-editor.log')}"], env=dict(os.environ, HROT_ANIMLAB_JOB=jp))
    while proc.poll() is None:
        f = free_gb()
        if f is not None and f < a.min_free_gb:
            print(f"free memory {f:.1f} GB - stopping"); subprocess.run(["taskkill", "/F", "/T", "/PID", str(proc.pid)], capture_output=True); break
        time.sleep(5)
    print(f"editor exit {proc.wait()} after {time.time() - t:.0f} s")
    if os.path.exists(out):
        r = json.load(open(out, encoding="utf-8"))
        print(json.dumps({"ok": r.get("ok"), "why": (r.get("why") or "")[-800:], "log": r.get("log"), "shots": r.get("shots")}, indent=1))
        for g in r.get("groups", []):
            print(g["name"], "missing:", [x["want"] for x in g["anims"] if not x["found"]])


if __name__ == "__main__":
    main()
