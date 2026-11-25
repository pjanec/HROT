// CE-4651 (U-18y) — draws entity cards (HrotCards) in the 3-D world, the way HROT does (FDP/Engine/Fdp.Presentation/Vis3D/CardOcclusion.cs,
// HROT docs/DESIGN_Map_3D_Mode.md §6am): a card is painted ONCE into its own render target (the frame, rows, NATO symbol and the leader line
// down to the anchor, the anchor at the texture's centre) and shown as a screen-space billboard at the anchor — constant pixel size, and
// DEPTH-TESTED at the anchor's depth, so terrain, buildings and vehicles in front of the entity hide its card (trees too, as in HROT).
// ⭐ R-2084 (no allocation on the hot path): a card is rebuilt only when the caller's input key changes, re-laid out and repainted only when
//   its content or its distance-visible items change; a steady frame only moves the billboards.
#pragma once

#include "CoreMinimal.h"
#include "UObject/Object.h"
#include "HrotCards.h"
#include "HrotCardLayer.generated.h"

class UMaterialBillboardComponent;
class UTextureRenderTarget2D;
class UMaterialInstanceDynamic;
class UMaterialInterface;

USTRUCT()
struct FHrotCardSlot
{
	GENERATED_BODY()
	UPROPERTY() TObjectPtr<UMaterialBillboardComponent> Billboard;
	UPROPERTY() TObjectPtr<UTextureRenderTarget2D> Target;
	UPROPERTY() TObjectPtr<UMaterialInstanceDynamic> Mid;
	HrotCards::FCard Card;
	uint32 InputKey = 0, CardHash = 0;
	uint64 PaintedMask = ~uint64(0);   // the visible-item mask the target was painted with
	FIntPoint TexPx = FIntPoint::ZeroValue;   // the card's texture in card pixels (not supersampled)
	FVector2f SizeSet = FVector2f::ZeroVector; // the billboard's screen size last set
	bool bShown = false, bSeen = false;
	double LastSeen = 0;
};

UCLASS()
class HROTDPS_API UHrotCardLayer : public UObject
{
	GENERATED_BODY()
public:
	/** the card material (/Game/Hrot/Fx/Labels/M_HrotCard: AlphaComposite, unlit, depth-tested; texture parameter "Card") */
	UPROPERTY() TObjectPtr<UMaterialInterface> Material;

	/** start a frame: the camera now (location cm, horizontal FOV degrees, viewport px) */
	void Begin(UWorld* World, const FVector& CameraCm, float HorizontalFovDeg, FIntPoint ViewportPx, float P00 = 0, float P11 = 0);
	/** one card this frame at Anchor (Unreal cm, the drawn body's top); Build fills the card, called only when InputKey changed */
	void Show(int64 EntityId, uint32 InputKey, const FVector& Anchor, TFunctionRef<void(HrotCards::FCard&)> Build);
	/** end the frame: cards not shown this frame are hidden (and released after a while unseen) */
	void End();
	/** hide and release every card */
	void Clear();

	int32 Shown() const { return NumShown; }
	int32 Repaints = 0, Rebuilds = 0;
	/** "<n> cards, <r> repaints, <b> rebuilds" */
	FString Describe() const;

	/** U-18y — a sheet of every affiliation x role, then the echelons, mobility and secondary marks, painted the way cards paint and saved as
	 *  a PNG (a visual check against HROT's SymbolRenderer); also two sample cards. False without a renderer */
	UFUNCTION(BlueprintCallable, Category = "HROT|Labels", meta = (WorldContext = "WorldContext"))
	static bool ExportSymbolSheet(UObject* WorldContext, const FString& FilePath);

	/** the billboard element's (BaseSizeX = vertical, BaseSizeY = horizontal) size so a texture of TexPx pixels shows 1:1 on screen (exposed for the rail) */
	static FVector2f BillboardSize(FIntPoint TexPx, float P00, float P11, FIntPoint ViewportPx);

private:
	UPROPERTY() TMap<int64, FHrotCardSlot> Slots;
	UPROPERTY() TObjectPtr<AActor> Holder;
	TWeakObjectPtr<UWorld> ForWorld;
	FVector Camera = FVector::ZeroVector;
	float Fov = 90.f, TanHalfV = 0.5f, P00 = 1.f;
	FIntPoint Viewport = FIntPoint(1280, 720);
	double Now = 0;
	int32 NumShown = 0;
	bool bCanPaint = false;
	void Paint(FHrotCardSlot& S, const HrotCards::FLayout& L);
	void SetSize(FHrotCardSlot& S);
};
