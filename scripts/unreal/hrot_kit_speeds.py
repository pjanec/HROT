"""hrot_kit_speeds.py - CE-4651 (U-19h 20): set every person kit's WalkSpeed / RunSpeed / CrouchSpeed to the clips' OWN ground speeds.

The kits carried the class defaults (1.5 / 3.6 / 1.2 m/s), never measured; the play rate is ground speed / these, so the legs ran ~2x too fast and the feet slid.
Measured here: the travel of the `root` bone over the clip (first to last frame, cm -> m, over the play length). Cross-checked on MF_Rifle_Walk_Fwd by the
planted feet (the stance foot stays put in clip space while `root` moves 0.9 m in 0.3 s = 3.0 m/s): walk 3.04, jog 6.23 m/s.
CrawlSpeed is left alone (the prone clip's root travel, 0.23 m/s, is a hip sway, not a crawl: not trusted).

    UnrealEditor-Cmd <project> -ExecutePythonScript=scripts/unreal/hrot_kit_speeds.py -nullrhi -unattended
"""
import math
import unreal

AL = unreal.AnimationLibrary


def ground_speed(anim):
    L = anim.get_play_length()
    a = AL.get_bone_pose_for_time(anim, "root", 0.0, False).translation
    b = AL.get_bone_pose_for_time(anim, "root", L, False).translation
    return math.hypot(b.x - a.x, b.y - a.y) / 100.0 / L


def mean(vals):
    return sum(vals) / len(vals) if vals else None


ar = unreal.AssetRegistryHelpers.get_asset_registry()
for a in ar.get_assets_by_path("/Game/Hrot/People", recursive=False):
    if not str(a.asset_name).startswith("DA_Person_"):
        continue
    kit = a.get_asset()
    clips = kit.get_editor_property("clips")
    by = {str(k): v for k, v in clips.items()}
    def of(*roles):
        return mean([ground_speed(by[r]) for r in roles if r in by and by[r]])
    walk = of("Rifle.Stand.Walk", "Unarmed.Stand.Walk")
    run = of("Rifle.Stand.Run", "Unarmed.Stand.Run")
    crouch = of("Rifle.Crouch.Walk", "Unarmed.Crouch.Walk")
    was = (kit.get_editor_property("walk_speed"), kit.get_editor_property("run_speed"), kit.get_editor_property("crouch_speed"))
    if walk: kit.set_editor_property("walk_speed", round(walk, 2))
    if run: kit.set_editor_property("run_speed", round(run, 2))
    if crouch: kit.set_editor_property("crouch_speed", round(crouch, 2))
    unreal.EditorAssetLibrary.save_loaded_asset(kit)
    unreal.log_warning("[HROT-SPEEDS] %s walk %.2f->%s run %.2f->%s crouch %.2f->%s" % (a.asset_name, was[0], walk and round(walk, 2), was[1], run and round(run, 2), was[2], crouch and round(crouch, 2)))
