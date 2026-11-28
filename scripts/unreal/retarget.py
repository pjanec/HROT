#!/usr/bin/env python3
"""retarget.py — CE-4651 (U-16 step 3): the anim lab's clip set retargeted from the mannequin onto another body (hrot_retarget.py).

    python scripts/unreal/retarget.py --project D:/Work/HrotUnreal/HrotUnreal.uproject --work <folder>
        [--target /Game/CitySampleCrowd/Character/Male/NormalWeight/Meshes/m_tal_nrw_body]
        [--rig /Game/Hrot/People/Rig/IK_CrowdBody] [--dest /Game/Hrot/People/Anims]

The clips are every name the anim lab uses (animlab.GROUPS), so the dressed people play the same states as the mannequin.
"""
import argparse, json, os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from animlab import GROUPS


def clip_names():
    out = []
    for g in GROUPS:
        for a in g["anims"]:
            specs = [a] if isinstance(a, str) else [a, a.get("then") or {}] + list(a.get("seq") or [])
            for s in specs:
                for k in (["_"] if isinstance(s, str) else ("base", "upper", "action")):
                    n = s if isinstance(s, str) else s.get(k)
                    if n and n not in out: out.append(n)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", required=True); ap.add_argument("--work", required=True)
    ap.add_argument("--engine", default=r"C:\UE_5.8\UE_5.8")
    ap.add_argument("--target", default="/Game/CitySampleCrowd/Character/Male/NormalWeight/Meshes/m_tal_nrw_body")
    ap.add_argument("--rig", default="/Game/Hrot/People/Rig/IK_CrowdBody")
    ap.add_argument("--dest", default="/Game/Hrot/People/Anims")
    ap.add_argument("--body-spec", default="", help="a json file: extra job keys for this body (chains, root, alignTarget)")
    ap.add_argument("--fix-only", action="store_true", help="only turn root motion off on what is in --dest")
    a = ap.parse_args()
    os.makedirs(a.work, exist_ok=True)
    fwd = lambda p: os.path.abspath(p).replace("\\", "/")
    out = os.path.join(a.work, "retarget-result.json")
    job = {"target": a.target, "rig": a.rig, "dest": a.dest, "anims": clip_names(), "out": fwd(out), "fixOnly": a.fix_only}
    if a.body_spec: job.update(json.load(open(a.body_spec, encoding="utf-8")))
    jp = os.path.join(a.work, "retarget-job.json"); json.dump(job, open(jp, "w"), indent=1)
    if os.path.exists(out): os.remove(out)
    exe = os.path.join(a.engine, "Engine", "Binaries", "Win64", "UnrealEditor-Cmd.exe")
    rc = subprocess.run([exe, a.project, "-run=pythonscript", f"-script={os.path.join(HERE, 'hrot_retarget.py')}", "-nullrhi",
                         "-unattended", "-nosplash", f"-abslog={os.path.join(a.work, 'retarget-editor.log')}"],
                        env=dict(os.environ, HROT_RETARGET_JOB=jp)).returncode
    print("editor exit", rc)
    if os.path.exists(out):
        r = json.load(open(out, encoding="utf-8"))
        print(json.dumps({k: r.get(k) for k in ("ok", "log", "missing")}, indent=1)); print((r.get("why") or "")[-1500:])
        print(len(r.get("retargeted") or []), "assets")


if __name__ == "__main__":
    main()
