"""hrot_glass.py - CE-4651 (U-20): the house glass /Game/Hrot/M_HrotGlass made a light, sky-reflecting grey-blue (it was near-black, the same as an
unlit room behind it, so a BROKEN window could not be told from an intact one - measured on bt-range 2026-11-24).

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi        (clear the read-only flag on M_HrotGlass.uasset first)
"""
import unreal
m = unreal.load_asset("/Game/Hrot/M_HrotGlass")
MEL = unreal.MaterialEditingLibrary
base = MEL.get_material_property_input_node(m, unreal.MaterialProperty.MP_BASE_COLOR)
base.set_editor_property("constant", unreal.LinearColor(0.34, 0.40, 0.44, 1.0))
rough = MEL.get_material_property_input_node(m, unreal.MaterialProperty.MP_ROUGHNESS)
rough.set_editor_property("r", 0.04)
MEL.recompile_material(m)
unreal.log_warning(f"[HROT-GLASS] saved {unreal.EditorAssetLibrary.save_loaded_asset(m, False)}")
