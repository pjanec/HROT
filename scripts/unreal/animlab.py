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

# the Modern / Modular Insurgent bodies (Fab, one maker, UE4-mannequin skeletons, textured: hrot_person.BODY_MESHES)
INSURGENT_STATES = [
    {"label": "rifle lowered", "base": "MM_Rifle_Idle_Hipfire", "weapon": "rifle"},
    {"label": "walk rifle aimed", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle"},
    {"label": "jog rifle aimed", "base": "MF_Rifle_Jog_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle"},
    {"label": "crouch-walk aimed", "base": "MM_Rifle_Crouch_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle"},
    {"label": "aimed yaw -45", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "yaw": -45, "weapon": "rifle"},
    {"label": "unarmed walk", "base": "MM_Unarmed_Walk_Fwd"},
    {"label": "prone", "base": "RTG_Prone_Idle.*"},
    {"label": "dead front", "base": "RTG_Death_From_The_Front.*", "loop": False, "pose": 8}]

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
    # ⭐ Mixamo, retargeted onto the mannequin (scripts/unreal/hrot_mixamo.py): prone, crawl, kneel, phone, idles
    {"name": "mixamo", "anims": [
        {"label": "prone idle", "base": "RTG_Prone_Idle.*"},
        {"label": "prone crawl", "base": "RTG_Prone_Forward_mixamo.*"},
        {"label": "crouch to prone", "base": "RTG_Crouch_To_Prone.*"},
        {"label": "stand to kneel", "base": "RTG_Rifle_Stand_To_Kneel.*"},
        {"label": "talking on phone", "base": "RTG_Talking_On_Phone.*"},
        {"label": "looking around", "base": "RTG_Looking_Around.*"},
        {"label": "waving", "base": "RTG_Waving.*"}]},
    # ⭐ U-16 step 2: state CHANGES blend — posed in one state, switched, shot `after` seconds into the change
    {"name": "transitions", "anims": [
        {"label": "walk>jog +0.1s", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle",
         "then": {"base": "MF_Rifle_Jog_Fwd"}, "after": 0.1},
        {"label": "walk>jog +0.6s", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle",
         "then": {"base": "MF_Rifle_Jog_Fwd"}, "after": 0.6},
        {"label": "lowered>aimed +0.1s", "base": "MM_Rifle_Idle_Hipfire", "weapon": "rifle",
         "then": {"upper": "MM_Rifle_Idle_ADS"}, "after": 0.1},
        {"label": "aim 0>90 +0.15s", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle",
         "then": {"yaw": 90}, "after": 0.15},
        {"label": "aim 0>90 +0.6s", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle",
         "then": {"yaw": 90}, "after": 0.6},
        {"label": "stand>prone +0.12s", "base": "RTG_Neutral_Idle.*", "then": {"base": "RTG_Prone_Idle.*"}, "after": 0.12},
        {"label": "stand>prone +0.5s", "base": "RTG_Neutral_Idle.*", "then": {"base": "RTG_Prone_Idle.*"}, "after": 0.5}]},
    # ⭐ U-16 step 3: DRESSED people — City Sample crowd bodies (BP_Person_NN: body + outfit on a leader pose), the same states, the
    # clips retargeted onto their skeleton (retarget.py → /Game/Hrot/People/Anims, same names)
    {"name": "dressed", "anims": [
        {"label": "walk unarmed", "base": "MM_Unarmed_Walk_Fwd", "body": "BP_Person_00"},
        {"label": "on the phone", "base": "RTG_Talking_On_Phone.*", "body": "BP_Person_01"},
        {"label": "waving", "base": "RTG_Waving.*", "body": "BP_Person_02"},
        {"label": "walk rifle aimed", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "BP_Person_03"},
        {"label": "crouch-walk aimed", "base": "MM_Rifle_Crouch_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "BP_Person_04"},
        {"label": "pistol aimed yaw 45", "base": "MM_Pistol_Idle_Hipfire", "upper": "MM_Pistol_Idle_ADS", "yaw": 45, "weapon": "pistol",
         "body": "BP_Person_05"},
        {"label": "prone", "base": "RTG_Prone_Idle.*", "body": "BP_Person_06"}]},
    # ⭐ the US soldier (Fab "US_Soldier": its own skeleton, mannequin proportions; clips retargeted → /Game/Hrot/People/Anims_USSoldier)
    {"name": "soldier", "anims": [
        {"label": "rifle lowered", "base": "MM_Rifle_Idle_Hipfire", "weapon": "rifle", "body": "US_Soldier"},
        {"label": "walk rifle aimed", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "US_Soldier"},
        {"label": "crouch-walk aimed", "base": "MM_Rifle_Crouch_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "US_Soldier"},
        {"label": "aimed yaw -45", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "yaw": -45, "weapon": "rifle", "body": "US_Soldier"},
        {"label": "jog + reload", "base": "MF_Rifle_Jog_Fwd", "upper": "MM_Rifle_Idle_Hipfire", "action": "MM_Rifle_Reload_Additive", "weapon": "rifle",
         "body": "US_Soldier"},
        {"label": "pistol aimed", "base": "MM_Pistol_Idle_Hipfire", "upper": "MM_Pistol_Idle_ADS", "weapon": "pistol", "body": "US_Soldier"},
        {"label": "prone", "base": "RTG_Prone_Idle.*", "body": "US_Soldier"}]},
    # ⭐ the Middle East fighter (Fab, a bare skeletal mesh with a Rigify skeleton: chains by hand, T-pose arms aligned —
    # hrot_person.BODY_MESHES; clips → /Game/Hrot/People/Anims_Fighter)
    {"name": "fighter", "anims": [
        {"label": "rifle lowered", "base": "MM_Rifle_Idle_Hipfire", "weapon": "rifle", "body": "Fighter"},
        {"label": "walk rifle aimed", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "Fighter"},
        {"label": "jog rifle aimed", "base": "MF_Rifle_Jog_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "Fighter"},
        {"label": "crouch-walk aimed", "base": "MM_Rifle_Crouch_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "Fighter"},
        {"label": "aimed yaw -45", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "yaw": -45, "weapon": "rifle", "body": "Fighter"},
        {"label": "unarmed walk", "base": "MM_Unarmed_Walk_Fwd", "body": "Fighter"},
        {"label": "prone", "base": "RTG_Prone_Idle.*", "body": "Fighter"},
        {"label": "dead front", "base": "RTG_Death_From_The_Front.*", "loop": False, "pose": 8, "body": "Fighter"}]},
    # ⭐ the Modern Insurgent (Fab, a UE4-mannequin skeleton, textured: hrot_person.BODY_MESHES; clips → Anims_Insurgent)
    *[{"name": name, "anims": [dict(a, body=body) for a in INSURGENT_STATES]} for name, body in (("insurgent", "Insurgent"), ("insurgent2", "Insurgent2"))],
    # ⭐ the fighting SIDES at first glance (UHrotPersonState.MaterialOverrides: a tint of the pack's camo per side)
    {"name": "sides", "anims": [
        {"label": "A walk aimed", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "US_Soldier", "side": "A"},
        {"label": "A rifle lowered", "base": "MM_Rifle_Idle_Hipfire", "weapon": "rifle", "body": "US_Soldier", "side": "A"},
        {"label": "A crouch aimed", "base": "MM_Rifle_Crouch_Idle", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "US_Soldier", "side": "A"},
        {"label": "B walk aimed", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "US_Soldier", "side": "B"},
        {"label": "B rifle lowered", "base": "MM_Rifle_Idle_Hipfire", "weapon": "rifle", "body": "US_Soldier", "side": "B"},
        {"label": "B crouch aimed", "base": "MM_Rifle_Crouch_Idle", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "US_Soldier", "side": "B"}]},
    # ⭐ DYING → DEAD: a death clip played ONCE (loop False) and held — `pose` seconds into it (past its end = dead)
    {"name": "dying", "anims": [
        {"label": "falling front", "base": "MM_Death_Front_01", "loop": False, "pose": 0.45, "weapon": "rifle", "body": "US_Soldier", "side": "B"},
        {"label": "dead front 1", "base": "MM_Death_Front_01", "loop": False, "pose": 6, "weapon": "rifle", "body": "US_Soldier", "side": "B"},
        {"label": "dead front 2", "base": "MM_Death_Front_02", "loop": False, "pose": 6, "weapon": "rifle", "body": "US_Soldier", "side": "B"},
        {"label": "dead back", "base": "MM_Death_Back_01", "loop": False, "pose": 6, "weapon": "rifle", "body": "US_Soldier", "side": "B"},
        {"label": "dead left", "base": "MM_Death_Left_01", "loop": False, "pose": 6, "weapon": "rifle", "body": "US_Soldier", "side": "B"},
        {"label": "dead right", "base": "MM_Death_Right_01", "loop": False, "pose": 6, "weapon": "rifle", "body": "US_Soldier", "side": "B"},
        {"label": "dying (mixamo)", "base": "RTG_Dying_mixamo.*", "loop": False, "pose": 8, "body": "US_Soldier", "side": "B"}]},
    # ⭐ the Mixamo deaths (user, 2026-11-19): standing front/back (body and headshot), crouching, prone — each falling and dead
    {"name": "deaths", "anims": [
        {"label": f"{lab} {'falling' if t < 1 else 'dead'}", "base": clip, "loop": False, "pose": t, "body": "US_Soldier", "side": "B"}
        for lab, clip in (("front", "RTG_Death_From_The_Front.*"), ("back", "RTG_Death_From_The_Back.*"),
                          ("front head", "RTG_Death_From_Front_Headshot.*"), ("back head", "RTG_Death_From_Back_Headshot.*"),
                          ("crouch head", "RTG_Death_Crouching_Headshot_Front.*"), ("crouch", "RTG_Crouch_Death.*"),
                          ("prone", "RTG_Prone_Death.*"))
        for t in (0.5, 8)]},
    # ⭐ the GRENADE throw: Lyra's full-body toss played once, at three moments; the additive toss on a walk
    {"name": "throw", "anims": [
        {"label": "toss 0.3 s", "base": "MM_Rifle_GrenadeToss", "loop": False, "pose": 0.3, "weapon": "rifle", "body": "US_Soldier", "side": "A"},
        {"label": "toss 0.6 s", "base": "MM_Rifle_GrenadeToss", "loop": False, "pose": 0.6, "weapon": "rifle", "body": "US_Soldier", "side": "A"},
        {"label": "toss 0.9 s", "base": "MM_Rifle_GrenadeToss", "loop": False, "pose": 0.9, "weapon": "rifle", "body": "US_Soldier", "side": "A"},
        {"label": "walk + toss", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_Hipfire", "action": "MM_Rifle_GrenadeToss_Additive",
         "weapon": "rifle", "body": "US_Soldier", "side": "A"},
        {"label": "overhand 0.6 s", "base": "RTG_Throw_mixamo.*", "loop": False, "pose": 0.6, "body": "US_Soldier", "side": "A"},
        {"label": "overhand 1.0 s", "base": "RTG_Throw_mixamo.*", "loop": False, "pose": 1.0, "body": "US_Soldier", "side": "A"},
        {"label": "overhand 1.4 s", "base": "RTG_Throw_mixamo.*", "loop": False, "pose": 1.4, "body": "US_Soldier", "side": "A"},
        {"label": "hit heavy", "base": "MM_HitReact_Front_Hvy_01", "loop": False, "pose": 0.35, "weapon": "rifle", "body": "US_Soldier", "side": "A"}]},
    # ⭐ SEATED (U-17j): the user's Mixamo "Driving" clip — the rider's pose on a deer / the dragon (a car seat, close enough)
    {"name": "seated", "anims": [
        {"label": "seated (driving)", "base": "RTG_Driving_mixamo.*", "body": "US_Soldier", "side": "A"},
        {"label": "seated, rifle", "base": "RTG_Driving_mixamo.*", "weapon": "rifle", "body": "US_Soldier", "side": "A"}]},
    # the aim pitch seen from the SIDE (facing 90: the barrel crosses the picture) — to judge the angle, not foreshortened
    {"name": "pitch-side", "anims": [
        {"label": "pitch 0", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "pitch": 0, "facing": 90, "weapon": "rifle"},
        {"label": "pitch +10", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "pitch": 10, "facing": 90, "weapon": "rifle"},
        {"label": "pitch +25", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "pitch": 25, "facing": 90, "weapon": "rifle"},
        {"label": "pitch -25", "base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Idle_ADS", "pitch": -25, "facing": 90, "weapon": "rifle"}]},
    # ⭐ PLAY MODE (Simulate): the engine ticks the people itself (no explicit posing); every `seq` entry is a state applied live,
    # held `hold` s, then shot (live-<n>.png); `speed` cm/s moves the actor along its facing (the clip plays in place)
    {"name": "live", "live": True, "anims": [
        {"label": "soldier", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "body": "US_Soldier", "fire": True, "seq": [
            {"base": "MF_Rifle_Walk_Fwd", "speed": 150}, {"base": "MF_Rifle_Jog_Fwd", "speed": 350},
            {"base": "MM_Rifle_Crouch_Walk_Fwd", "speed": 120}, {"base": "MM_Rifle_Idle_Hipfire", "yaw": 60, "speed": 0},
            {"base": "RTG_Prone_Idle.*", "upper": None, "yaw": 0, "speed": 0}]},
        {"label": "civilian", "base": "MM_Unarmed_Walk_Fwd", "body": "BP_Person_00", "seq": [
            {"base": "MM_Unarmed_Walk_Fwd", "speed": 150}, {"base": "MM_Unarmed_Jog_Fwd", "speed": 350},
            {"base": "RTG_Looking_Around.*", "speed": 0}, {"base": "RTG_Talking_On_Phone.*", "speed": 0},
            {"base": "RTG_Waving.*", "speed": 0}]},
        {"label": "mannequin", "base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle", "seq": [
            {"base": "MF_Rifle_Walk_Fwd", "speed": 150}, {"base": "MF_Rifle_Jog_Fwd", "speed": 350},
            {"base": "MM_Rifle_Idle_Hipfire", "upper": "MM_Rifle_Fire", "speed": 0}, {"base": "MM_Rifle_Idle_Hipfire", "upper": None, "speed": 0},
            {"base": "MM_Rifle_Crouch_Idle", "speed": 0}]}]},
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", required=True); ap.add_argument("--shots", required=True)
    ap.add_argument("--engine", default=r"C:\UE_5.8\UE_5.8"); ap.add_argument("--min-free-gb", type=float, default=6.0)
    ap.add_argument("--blast-trial", action="store_true", help="the live phase sets off every explosion system of the VFX pack instead")
    ap.add_argument("--blasts", default="", help="the live phase sets off these instead: Effect@scale[@KIND],… (Effect = a name in the VFX "
                                                  "pack's Explosion folder; KIND = GRENADE / ROCKET / MORTAR / TANK_HE for the flash, default MORTAR)")
    ap.add_argument("--only", default="", help="comma-separated group names to render (default: all)")
    a = ap.parse_args()
    os.makedirs(a.shots, exist_ok=True)
    fwd = lambda p: os.path.abspath(p).replace("\\", "/")
    out = os.path.join(a.shots, "animlab-result.json")
    groups = [g for g in GROUPS if not a.only or g["name"] in a.only.split(",")]
    job = {"groups": groups, "shots": fwd(a.shots), "out": fwd(out), "settleSeconds": 20}
    if a.blast_trial:   # each candidate once, at scale 1, with the mortar's flash and scorch; pictured at 0.15, 0.6 and 2 s
        d = "/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Explosion/"
        job["blasts"] = [{"name": n, "kind": "MORTAR", "effect": d + n + "." + n, "scale": 1.0}
                         for n in ("P_Explosion_Big_A", "P_Explosion_Big_B", "P_Explosion_Big_C", "P_Explosion_Side", "P_Explosion_Smoke", "P_Molotov")]
        job["blastPics"] = [0.15, 0.6, 2.0]; job["blastSpacing"] = 4000
    if a.blasts:
        d = "/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Explosion/"
        job["blasts"] = []
        for b in a.blasts.split(","):
            n, sc, *k = b.split("@")
            job["blasts"].append({"name": f"{n}x{sc}", "kind": (k[0] if k else "MORTAR"), "effect": d + n + "." + n, "scale": float(sc)})
        job["blastPics"] = [0.15, 0.6, 2.0]; job["blastSpacing"] = 4000
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
