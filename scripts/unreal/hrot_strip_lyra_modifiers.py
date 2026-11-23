"""hrot_strip_lyra_modifiers.py - CE-4651 (U-18s): remove Lyra's ANIMATION MODIFIER records from OUR animations (/Game/Hrot), so a packaged
client cooks (run in the editor: -ExecutePythonScript; HROT_STRIP_DRY=1 = only list).

An animation copied from Lyra carries an AnimationModifiersAssetUserData naming FootstepEffectTagModifier - an EDITOR-only record that the
modifier was applied (its footstep notifies stay in the clip). The modifier is a Lyra Blueprint needing LyraGame, which this project does not
have: the cooker loads it and fails (measured: the base client's cook, U-18s). Removing the record changes nothing at runtime.
Writes the list of changed assets to HROT_STRIP_OUT (default <Saved>/hrot_strip_lyra_modifiers.txt).
"""
import os
import unreal

DRY = os.environ.get("HROT_STRIP_DRY") == "1"
OUT = os.environ.get("HROT_STRIP_OUT") or os.path.join(unreal.Paths.project_saved_dir(), "hrot_strip_lyra_modifiers.txt")
AR = unreal.AssetRegistryHelpers.get_asset_registry()
AR.wait_for_completion()
flt = unreal.ARFilter(package_paths=["/Game/Hrot"], recursive_paths=True, class_paths=[unreal.TopLevelAssetPath("/Script/Engine", "AnimSequence")])
changed, skipped = [], 0
for ad in AR.get_assets(flt):
    anim = ad.get_asset()
    data = list(anim.get_editor_property("asset_user_data") or [])
    def lyra(d):   # a modifier record naming Lyra's footstep modifier (every animation carries the container, most hold nothing of Lyra)
        if d is None or d.get_class().get_name() != "AnimationModifiersAssetUserData": return False
        try: inst = d.get_editor_property("animation_modifier_instances") or []
        except Exception: return False
        return any(m is not None and "FootstepEffectTagModifier" in m.get_class().get_name() for m in inst)
    keep = [d for d in data if not lyra(d)]
    if len(keep) == len(data):
        skipped += 1
        continue
    changed.append(str(ad.package_name))
    if not DRY:
        anim.set_editor_property("asset_user_data", keep)
        unreal.EditorAssetLibrary.save_loaded_asset(anim, False)
open(OUT, "w").write(("DRY RUN - " if DRY else "") + f"{len(changed)} animations without the modifier record, {skipped} had none\n" + "\n".join(changed))
unreal.log_warning(f"[HROT-STRIP] {len(changed)} changed ({'dry' if DRY else 'saved'}), {skipped} untouched -> {OUT}")
unreal.SystemLibrary.quit_editor()
