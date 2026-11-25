"""hrot_glass.py - CE-4651 (U-20): the house glass /Game/Hrot/M_HrotGlass.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi        (clear the read-only flag on M_HrotGlass.uasset first)

History: it was near-black (an unlit room behind it looked the same, so a BROKEN window could not be told from an intact one - bt-range
2026-11-24) -> a light, sky-reflecting grey-blue, OPAQUE. ⭐ 2026-11-25, the user live on Stechovice: "no transparent windows" -> TRANSLUCENT:
a faint grey-blue tint (opacity 0.25) -> 2026-11-26 in the packaged game that pane mirrored the sky WHITE: now a dark tint (0.03), opacity 0.2,
specular 0.2, lit per pixel so it still catches the sky; a broken window is the empty opening (the client removes the
glass triangles), still told apart from the tinted pane. The house meshes are not Nanite (HrotTerrainLibrary.BuildMesh), so translucency draws.
"""
import unreal
m = unreal.load_asset("/Game/Hrot/M_HrotGlass")
MEL = unreal.MaterialEditingLibrary
m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
m.set_editor_property("translucency_lighting_mode", unreal.TranslucencyLightingMode.TLM_SURFACE_PER_PIXEL_LIGHTING)
m.set_editor_property("two_sided", True)
base = MEL.get_material_property_input_node(m, unreal.MaterialProperty.MP_BASE_COLOR)
base.set_editor_property("constant", unreal.LinearColor(0.03, 0.04, 0.05, 1.0))   # 2026-11-26: a light pane mirrored the sky WHITE
rough = MEL.get_material_property_input_node(m, unreal.MaterialProperty.MP_ROUGHNESS)
rough.set_editor_property("r", 0.12)
spec = MEL.get_material_property_input_node(m, unreal.MaterialProperty.MP_SPECULAR)
if spec is None:
    spec = MEL.create_material_expression(m, unreal.MaterialExpressionConstant, -300, 400)
    MEL.connect_material_property(spec, "", unreal.MaterialProperty.MP_SPECULAR)
spec.set_editor_property("r", 0.2)   # less sky glare: the pane reads as glass you see through, not a white card
op = MEL.get_material_property_input_node(m, unreal.MaterialProperty.MP_OPACITY)
if op is None:
    op = MEL.create_material_expression(m, unreal.MaterialExpressionConstant, -300, 300)
    MEL.connect_material_property(op, "", unreal.MaterialProperty.MP_OPACITY)
op.set_editor_property("r", 0.2)
MEL.recompile_material(m)
unreal.log_warning(f"[HROT-GLASS] translucent, dark, opacity 0.2, specular 0.2; saved {unreal.EditorAssetLibrary.save_loaded_asset(m, False)}")
