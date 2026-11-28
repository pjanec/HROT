# CE-4651 (U-18y) - the entity-card material: /Game/Hrot/Fx/Labels/M_HrotCard (under /Game/Hrot/Fx, which the client always cooks).
#   UnrealEditor-Cmd <project> -ExecutePythonScript=scripts/unreal/hrot_card_material.py -unattended -nullrhi
# A card is painted into a render target by the canvas (HrotCardLayer): its RGB is the card PREMULTIPLIED over black (SE_BLEND_AlphaBlend
# items), its alpha accumulated - except text: a runtime font writes RGB only (SE_BLEND_TranslucentAlphaOnly, BatchedElements.cpp), so the
# opacity is max(a, r, g, b): a bare name (no frame, 1-2 m/px) is still opaque where its glyphs are.
#   Emissive = rgb * EyeAdaptationInverse (no exposure drift) ; Opacity = max(a, r, g, b) ; AlphaComposite, unlit, two-sided, after DOF,
#   no fog - and it stays DEPTH-TESTED (the default), so terrain, buildings and vehicles in front hide the card (HROT's per-pixel rule).
import unreal

PATH = "/Game/Hrot/Fx/Labels/M_HrotCard"
AT = unreal.AssetToolsHelpers.get_asset_tools()
MEL = unreal.MaterialEditingLibrary
EAL = unreal.EditorAssetLibrary


def prop(o, name, value):
    try:
        o.set_editor_property(name, value); return True
    except Exception as e:
        unreal.log_warning(f"[HROT-CARD] {name}: {e}"); return False


if EAL.does_asset_exist(PATH):
    EAL.delete_asset(PATH)
folder, name = PATH.rsplit("/", 1)
m = AT.create_asset(name, folder, unreal.Material, unreal.MaterialFactoryNew())
prop(m, "blend_mode", unreal.BlendMode.BLEND_ALPHA_COMPOSITE)
prop(m, "shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
prop(m, "two_sided", True)
prop(m, "translucency_pass", unreal.MaterialTranslucencyPass.MTP_AFTER_DOF)
prop(m, "use_translucency_vertex_fog", False)
prop(m, "compute_fog_per_pixel", False)

E = lambda cls, x, y: MEL.create_material_expression(m, cls, x, y)
tex = E(unreal.MaterialExpressionTextureSampleParameter2D, -800, 0)
prop(tex, "parameter_name", "Card")
prop(tex, "texture", unreal.load_asset("/Engine/EngineResources/WhiteSquareTexture"))
eye = E(unreal.MaterialExpressionEyeAdaptationInverse, -500, -200)
mul = E(unreal.MaterialExpressionMultiply, -250, -100)
MEL.connect_material_expressions(tex, "RGB", mul, "A")
MEL.connect_material_expressions(eye, "", mul, "B")
MEL.connect_material_property(mul, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
m1 = E(unreal.MaterialExpressionMax, -500, 150); MEL.connect_material_expressions(tex, "R", m1, "A"); MEL.connect_material_expressions(tex, "G", m1, "B")
m2 = E(unreal.MaterialExpressionMax, -350, 200); MEL.connect_material_expressions(m1, "", m2, "A"); MEL.connect_material_expressions(tex, "B", m2, "B")
m3 = E(unreal.MaterialExpressionMax, -200, 250); MEL.connect_material_expressions(m2, "", m3, "A"); MEL.connect_material_expressions(tex, "A", m3, "B")
MEL.connect_material_property(m3, "", unreal.MaterialProperty.MP_OPACITY)
MEL.recompile_material(m)
EAL.save_asset(PATH)
unreal.log_warning(f"[HROT-CARD] {PATH} written")
