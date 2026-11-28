// CE-4651 (U-18y) — see HrotCardLayer.h.
#include "HrotCardLayer.h"

#include "HrotRuntimeLibrary.h"
#include "CanvasItem.h"
#include "CanvasTypes.h"
#include "Components/MaterialBillboardComponent.h"
#include "Engine/Canvas.h"
#include "Engine/Engine.h"
#include "Engine/Font.h"
#include "Engine/TextureRenderTarget2D.h"
#include "Engine/World.h"
#include "Fonts/FontMeasure.h"
#include "Framework/Application/SlateApplication.h"
#include "GameFramework/Actor.h"
#include "Kismet/KismetRenderingLibrary.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Styling/CoreStyle.h"
#include "Misc/Paths.h"

namespace
{
	constexpr int32 CardSupersample = 2;   // painted at 2x, sampled bilinear at 1x -> antialiased (HROT's SymbolRenderer does the same)

	FLinearColor Lin(uint32 Rgba) { return FLinearColor(FColor(uint8(Rgba >> 24), uint8(Rgba >> 16), uint8(Rgba >> 8), uint8(Rgba))); }
	// Slate's font size is in points at 96 dpi: 1 pt = 4/3 px
	// ⚠ canvas text draws only with a UFont (FCanvasSimpleTextItem::HasValidText: Font != nullptr) — Slate's default style is a composite font
	//   with none, which drew NO text (measured: names and the symbols' marks missing). The engine's Roboto is a runtime UFont.
	FSlateFontInfo FontPx(float Px)
	{
		static UFont* Roboto = HrotLoadRooted<UFont>(TEXT("/Engine/EngineFonts/Roboto.Roboto"));   // rooted (a plain static dangles)
		const int32 Size = FMath::Max(1, FMath::RoundToInt(Px * 0.75f));
		return Roboto ? FSlateFontInfo(Roboto, Size) : FCoreStyle::GetDefaultFontStyle("Regular", Size);
	}

	// measured by the SAME item that draws it (at the supersampled size, halved): Slate's measure service disagreed with the canvas by
	// ~10 % (measured: a long name ran past its frame)
	float MeasureText(const FString& Text, float Px)
	{
		if (FSlateApplication::IsInitialized() && FApp::CanEverRender())
		{
			struct FMeasureItem : FCanvasTextItem { using FCanvasTextItem::FCanvasTextItem; using FCanvasTextItem::GetTextSize; };   // (protected there)
			FMeasureItem Item(FVector2D::ZeroVector, FText::FromString(Text), FontPx(Px * CardSupersample), FLinearColor::White);
			return float(Item.GetTextSize(1.f).X) / CardSupersample;
		}
		return 0.55f * Px * Text.Len();   // headless (a rail): a plausible width
	}

	/** HrotCards' canvas over an Unreal UCanvas on a render target, scaled by the supersample. Every item AlphaBlend: the target ends up
	 *  premultiplied over black with its alpha accumulated (BatchedElements.cpp SetBlendState) — except text (RGB only), which the
	 *  material's max(a, r, g, b) opacity covers. */
	class FCanvasSink final : public HrotCards::ICardCanvas
	{
	public:
		explicit FCanvasSink(UCanvas* InCanvas) : C(InCanvas) {}
		virtual void Polygon(TArrayView<const FVector2f> P, uint32 Fill, uint32 Outline, float Px) override
		{
			if ((Fill & 0xFF) && P.Num() >= 3)
			{
				TArray<FCanvasUVTri, TInlineAllocator<16>> Tris;
				const FLinearColor F = Lin(Fill);
				for (int32 I = 1; I + 1 < P.Num(); I++)
				{
					FCanvasUVTri T;
					T.V0_Pos = S(P[0]); T.V1_Pos = S(P[I]); T.V2_Pos = S(P[I + 1]);
					T.V0_Color = T.V1_Color = T.V2_Color = F;
					Tris.Add(T);
				}
				FCanvasTriangleItem Item(TArray<FCanvasUVTri>(Tris), GWhiteTexture);
				Item.BlendMode = SE_BLEND_AlphaBlend;
				C->DrawItem(Item);
			}
			if ((Outline & 0xFF) && Px > 0)
				for (int32 I = 0; I < P.Num(); I++) Line(P[I], P[(I + 1) % P.Num()], Outline, Px);
		}
		virtual void Line(FVector2f A, FVector2f B, uint32 Colour, float Px) override
		{
			FCanvasLineItem Item(S(A), S(B));
			Item.LineThickness = FMath::Max(1.f, Px) * CardSupersample;
			Item.SetColor(Lin(Colour));
			Item.BlendMode = SE_BLEND_AlphaBlend;
			C->DrawItem(Item);
		}
		virtual void Ellipse(FVector2f Ctr, float Rx, float Ry, uint32 Fill, uint32 Outline, float Px) override
		{
			FVector2f P[32];
			for (int32 I = 0; I < 32; I++) { const float A = I * UE_TWO_PI / 32.f; P[I] = Ctr + FVector2f(Rx * FMath::Cos(A), Ry * FMath::Sin(A)); }
			Polygon(P, Fill, Outline, Px);
		}
		virtual void Text(FVector2f Ctr, const FString& T, float Px, uint32 Colour) override
		{
			FCanvasTextItem Item(S(Ctr), FText::FromString(T), FontPx(Px * CardSupersample), Lin(Colour));
			Item.bCentreX = Item.bCentreY = true;
			C->DrawItem(Item);
		}
		virtual void Rect(FVector2f Min, FVector2f Size, uint32 Fill, uint32 Outline, float Px) override
		{
			auto Tile = [&](FVector2f M, FVector2f Sz, uint32 Col)
			{
				FCanvasTileItem Item(S(M), FVector2D(Sz.X * CardSupersample, Sz.Y * CardSupersample), Lin(Col));
				Item.BlendMode = SE_BLEND_AlphaBlend;
				C->DrawItem(Item);
			};
			if (Fill & 0xFF) Tile(Min, Size, Fill);
			if ((Outline & 0xFF) && Px > 0)
			{
				const float T = FMath::Min(Px, FMath::Min(Size.X, Size.Y) * 0.5f);
				Tile(Min, FVector2f(Size.X, T), Outline);
				Tile(FVector2f(Min.X, Min.Y + Size.Y - T), FVector2f(Size.X, T), Outline);
				Tile(FVector2f(Min.X, Min.Y + T), FVector2f(T, Size.Y - 2 * T), Outline);
				Tile(FVector2f(Min.X + Size.X - T, Min.Y + T), FVector2f(T, Size.Y - 2 * T), Outline);
			}
		}
		virtual void TextAt(FVector2f At, const FString& T, float Px, uint32 Colour) override
		{
			FCanvasTextItem Item(S(At), FText::FromString(T), FontPx(Px * CardSupersample), Lin(Colour));
			C->DrawItem(Item);
		}
	private:
		UCanvas* C;
		static FVector2D S(FVector2f P) { return FVector2D(P.X * CardSupersample, P.Y * CardSupersample); }
	};
}

FVector2f UHrotCardLayer::BillboardSize(FIntPoint TexPx, float P00, float P11, FIntPoint ViewportPx)
{
	// UMaterialBillboardComponent (bSizeIsInScreenSpace, MaterialBillboardComponent.cpp:166-171): with W the clip w at the anchor, the sprite's half extents are
	//   along view UP    : BaseSizeX * W              -> BaseSizeX * P11 * ViewH / 2 px
	//   along view RIGHT : BaseSizeY * AspectRatio * W -> BaseSizeY * (P00 / P11) * P00 * ViewW / 2 px      (AspectRatio = |clip y dir| / |clip x dir| = P00 / P11)
	// ⚠ BaseSizeX is the VERTICAL extent and BaseSizeY the HORIZONTAL one (the texture's U runs along view right): measured 2026-11-23 on a 1160 x 605 view - cards
	// came out wider than the texture by TexH / TexW and shorter by its inverse (user: "squashed, text stretched, cut on the left").
	return FVector2f(TexPx.Y / (P11 * FMath::Max(1, ViewportPx.Y)), TexPx.X * P11 / (P00 * P00 * FMath::Max(1, ViewportPx.X)));
}

void UHrotCardLayer::Begin(UWorld* World, const FVector& CameraCm, float HorizontalFovDeg, FIntPoint ViewportPx, float InP00, float InP11)
{
	if (ForWorld.Get() != World) { Clear(); ForWorld = World; }
	Camera = CameraCm; Fov = HorizontalFovDeg; Viewport = FIntPoint(FMath::Max(1, ViewportPx.X), FMath::Max(1, ViewportPx.Y));
	// the REAL projection when the caller has it (aspect constraints, the camera's own FOV); else from the FOV
	P00 = InP00 > 0 ? InP00 : 1.f / FMath::Tan(FMath::DegreesToRadians(FMath::Clamp(Fov, 1.f, 170.f)) * 0.5f);
	TanHalfV = InP11 > 0 ? 1.f / InP11 : (1.f / P00) * Viewport.Y / Viewport.X;
	Now = World ? World->GetRealTimeSeconds() : 0;
	bCanPaint = World && FApp::CanEverRender() && Material;
	NumShown = 0;
	if (World && !Holder)
	{
		FActorSpawnParameters P; P.ObjectFlags |= RF_Transient; P.Name = MakeUniqueObjectName(World, AActor::StaticClass(), TEXT("HrotCards"));
		Holder = World->SpawnActor<AActor>(AActor::StaticClass(), FTransform::Identity, P);
		if (Holder)
		{
			USceneComponent* Root = NewObject<USceneComponent>(Holder, TEXT("Root"));
			Holder->SetRootComponent(Root); Root->RegisterComponent();
		}
	}
	for (TPair<int64, FHrotCardSlot>& KV : Slots) KV.Value.bSeen = false;
}

void UHrotCardLayer::Show(int64 Id, uint32 InputKey, const FVector& Anchor, TFunctionRef<void(HrotCards::FCard&)> Build)
{
	if (!bCanPaint || !Holder) return;
	FHrotCardSlot& S = Slots.FindOrAdd(Id);
	S.bSeen = true; S.LastSeen = Now;
	if (S.InputKey != InputKey || S.Card.Items.Num() == 0)
	{
		S.Card.Items.Reset(); S.Card.EntityId = Id;
		Build(S.Card);
		S.InputKey = InputKey;
		const uint32 H = HrotCards::HashOf(S.Card);
		if (H != S.CardHash) { S.CardHash = H; S.PaintedMask = ~uint64(0); }
		Rebuilds++;
	}
	// metres per pixel at the anchor (HROT's cutoffs): 2 d tan(vfov / 2) / H
	const float Mpp = float(FVector::Dist(Camera, Anchor) / 100.0) * 2.f * TanHalfV / Viewport.Y;
	uint64 Mask = 0;
	for (int32 I = 0; I < S.Card.Items.Num() && I < 64; I++)
	{
		const float Cut = S.Card.Items[I].MaxMetresPerPixel;
		if (Cut <= 0 || Mpp <= Cut) Mask |= uint64(1) << I;
	}
	if (Mask == 0)
	{
		if (S.bShown && S.Billboard) { S.Billboard->SetVisibility(false); S.bShown = false; }
		return;
	}
	if (Mask != S.PaintedMask)
	{
		HrotCards::FLayout L;
		if (!HrotCards::Layout(S.Card, Mpp, MeasureText, L)) return;
		Paint(S, L);
		S.PaintedMask = Mask;
	}
	if (!S.Billboard) return;
	SetSize(S);
	S.Billboard->SetWorldLocation(Anchor);
	if (!S.bShown) { S.Billboard->SetVisibility(true); S.bShown = true; }
	NumShown++;
}

void UHrotCardLayer::Paint(FHrotCardSlot& S, const HrotCards::FLayout& L)
{
	UWorld* World = ForWorld.Get();
	if (!World) return;
	// the texture: the card over its leader line, the anchor at the centre (so the billboard's centre is the anchor)
	const int32 W = FMath::CeilToInt(L.W) + 2, Hc = FMath::CeilToInt(L.H + HrotCards::Leader) + 1;
	S.TexPx = FIntPoint(W, 2 * Hc);
	const FIntPoint Size = S.TexPx * CardSupersample;
	if (!S.Target)
	{
		S.Target = UKismetRenderingLibrary::CreateRenderTarget2D(World, Size.X, Size.Y, RTF_RGBA8, FLinearColor::Transparent, false);
		S.Target->ClearColor = FLinearColor::Transparent;
	}
	else if (S.Target->SizeX != Size.X || S.Target->SizeY != Size.Y) S.Target->ResizeTarget(Size.X, Size.Y);
	UKismetRenderingLibrary::ClearRenderTarget2D(World, S.Target, FLinearColor::Transparent);
	UCanvas* Canvas = nullptr; FVector2D CanvasSize; FDrawToRenderTargetContext Ctx;
	UKismetRenderingLibrary::BeginDrawCanvasToRenderTarget(World, S.Target, Canvas, CanvasSize, Ctx);
	if (Canvas)
	{
		FCanvasSink Sink(Canvas);
		const FVector2f Anchor(W * 0.5f, float(Hc));
		HrotCards::Paint(S.Card, L, FVector2f(Anchor.X - L.W * 0.5f, 0.f), Anchor, Sink);
	}
	UKismetRenderingLibrary::EndDrawCanvasToRenderTarget(World, Ctx);
	Repaints++;
	if (!S.Billboard)
	{
		S.Mid = UMaterialInstanceDynamic::Create(Material, this);
		S.Billboard = NewObject<UMaterialBillboardComponent>(Holder);
		S.Billboard->SetUsingAbsoluteLocation(true);
		S.Billboard->SetCastShadow(false);
		S.Billboard->SetupAttachment(Holder->GetRootComponent());
		S.Billboard->RegisterComponent();
		S.bShown = true;
	}
	S.Mid->SetTextureParameterValue(TEXT("Card"), S.Target);
	S.SizeSet = FVector2f::ZeroVector;   // the texture's size may have changed
}

void UHrotCardLayer::SetSize(FHrotCardSlot& S)
{
	// BaseSizeX = the VERTICAL extent, BaseSizeY the horizontal one (see BillboardSize); P11 = 1 / TanHalfV
	const FVector2f SizeXY = BillboardSize(S.TexPx, P00, 1.f / TanHalfV, Viewport);
	const FVector2f Want = SizeXY;
	if (Want.Equals(S.SizeSet, 1e-6f)) return;
	S.SizeSet = Want;
	TArray<FMaterialSpriteElement> E;
	FMaterialSpriteElement& El = E.AddDefaulted_GetRef();
	El.Material = S.Mid; El.bSizeIsInScreenSpace = true; El.BaseSizeX = Want.X; El.BaseSizeY = Want.Y;
	S.Billboard->SetElements(E);
}

void UHrotCardLayer::End()
{
	TArray<int64, TInlineAllocator<8>> Gone;
	for (TPair<int64, FHrotCardSlot>& KV : Slots)
	{
		FHrotCardSlot& S = KV.Value;
		if (S.bSeen) continue;
		if (S.bShown && S.Billboard) { S.Billboard->SetVisibility(false); S.bShown = false; }
		if (Now - S.LastSeen > 10.0) Gone.Add(KV.Key);   // an entity gone for good: its target released
	}
	for (int64 Id : Gone)
	{
		FHrotCardSlot& S = Slots[Id];
		if (S.Billboard) S.Billboard->DestroyComponent();
		if (S.Target) S.Target->ReleaseResource();
		Slots.Remove(Id);
	}
}

void UHrotCardLayer::Clear()
{
	for (TPair<int64, FHrotCardSlot>& KV : Slots)
	{
		if (KV.Value.Billboard && IsValid(KV.Value.Billboard)) KV.Value.Billboard->DestroyComponent();
		if (KV.Value.Target) KV.Value.Target->ReleaseResource();
	}
	Slots.Reset();
	if (Holder && IsValid(Holder)) Holder->Destroy();
	Holder = nullptr;
	NumShown = 0;
}

FString UHrotCardLayer::Describe() const
{
	return FString::Printf(TEXT("%d cards, %d repaints, %d rebuilds"), NumShown, Repaints, Rebuilds);
}

bool UHrotCardLayer::ExportSymbolSheet(UObject* WorldContext, const FString& FilePath)
{
	using namespace HrotCards;
	UWorld* World = GEngine ? GEngine->GetWorldFromContextObject(WorldContext, EGetWorldErrorMode::LogAndReturnNull) : nullptr;
	if (!World || !FApp::CanEverRender()) return false;
	constexpr int32 Cell = 64, Cols = 11, Rows = 4 + 3 + 3;
	const FIntPoint Size(Cols * Cell * CardSupersample, (Rows * Cell + 160) * CardSupersample);
	UTextureRenderTarget2D* T = UKismetRenderingLibrary::CreateRenderTarget2D(World, Size.X, Size.Y, RTF_RGBA8, FLinearColor(0.12f, 0.12f, 0.12f, 1.f), false);
	UKismetRenderingLibrary::ClearRenderTarget2D(World, T, FLinearColor(0.12f, 0.12f, 0.12f, 1.f));
	UCanvas* Canvas = nullptr; FVector2D CS; FDrawToRenderTargetContext Ctx;
	UKismetRenderingLibrary::BeginDrawCanvasToRenderTarget(World, T, Canvas, CS, Ctx);
	if (!Canvas) return false;
	FCanvasSink Sink(Canvas);
	auto At = [&](int32 C, int32 R) { return FVector2f(C * Cell + Cell * 0.5f, R * Cell + Cell * 0.5f); };
	// rows 0-3: the affiliations (friend, hostile, neutral, unknown) x the roles NoRole..Support, at 22 px, a company each
	const EAffiliation Affs[4] = { EAffiliation::Friend, EAffiliation::Hostile, EAffiliation::Neutral, EAffiliation::Unknown };
	for (int32 R = 0; R < 4; R++)
		for (int32 C = 0; C < Cols; C++)
		{
			FSymbol S; S.Affiliation = Affs[R]; S.Role = ERole(C); S.Echelon = EEchelon::Company;
			BuildSymbol(S, At(C, R), 22.f, Sink);
		}
	// rows 4-6: the echelons team..division (friend infantry), then mobility and the secondary marks
	for (int32 E = 1; E <= 9; E++) { FSymbol S; S.Affiliation = EAffiliation::Friend; S.Role = ERole::Infantry; S.Echelon = EEchelon(E); BuildSymbol(S, At(E - 1, 4), 22.f, Sink); }
	{ FSymbol S; S.Affiliation = EAffiliation::Friend; S.Role = ERole::Mechanized; S.Echelon = EEchelon::Platoon; S.Mobility = EMobility::Tracked; BuildSymbol(S, At(0, 5), 22.f, Sink); }
	{ FSymbol S; S.Affiliation = EAffiliation::Hostile; S.Role = ERole::Motorized; S.Echelon = EEchelon::Platoon; S.Mobility = EMobility::Wheeled; BuildSymbol(S, At(1, 5), 22.f, Sink); }
	const ERole Marks[7] = { ERole::AntiArmour, ERole::Mortar, ERole::Artillery, ERole::Reconnaissance, ERole::Armour, ERole::Headquarters, ERole::Support };
	for (int32 K = 0; K < 7; K++) { FSymbol S; S.Affiliation = EAffiliation::Friend; S.Role = ERole::Infantry; S.Echelon = EEchelon::Squad; S.Secondary = Marks[K]; BuildSymbol(S, At(K + 2, 5), 22.f, Sink); }
	// rows 7-9: sizes 12, 20, 32, 48 px
	const float Sizes[4] = { 12.f, 20.f, 32.f, 48.f };
	for (int32 K = 0; K < 4; K++) { FSymbol S; S.Affiliation = EAffiliation::Hostile; S.Role = ERole::Armour; S.Echelon = EEchelon::Battalion; BuildSymbol(S, FVector2f(K * 2.f * Cell + Cell, 8.5f * Cell), Sizes[K], Sink); }
	// two cards under the grid: a man's (frame, bar, name) and a unit's (symbol cell + name), with their leader lines
	auto Measure = [](const FString& Tx, float Px) { return MeasureText(Tx, Px); };
	FCard Man; Man.EntityId = 1043; Man.Frame(SideColour(2)); Man.Bar(10, 0.5f, 0xFFFF00FF, 0x282828DC); Man.Text(20, TEXT("T-72 #1043"), 0xF0F0F0FF);
	FCard Unit; Unit.EntityId = 7; Unit.Frame(SideColour(1));
	{ FSymbol S; S.Affiliation = EAffiliation::Friend; S.Role = ERole::Infantry; S.Echelon = EEchelon::Squad; Unit.Symbol(S, 22); }
	Unit.Text(20, TEXT("1st squad #7"), 0xF0F0F0FF);
	FLayout L;
	if (Layout(Man, 0, Measure, L)) HrotCards::Paint(Man, L, FVector2f(40, Rows * Cell + 20), FVector2f(40 + L.W * 0.5f, Rows * Cell + 20 + L.H + Leader), Sink);
	if (Layout(Unit, 0, Measure, L)) HrotCards::Paint(Unit, L, FVector2f(260, Rows * Cell + 20), FVector2f(260 + L.W * 0.5f, Rows * Cell + 20 + L.H + Leader), Sink);
	UKismetRenderingLibrary::EndDrawCanvasToRenderTarget(World, Ctx);
	UKismetRenderingLibrary::ExportRenderTarget(World, T, FPaths::GetPath(FilePath), FPaths::GetCleanFilename(FilePath));
	return true;
}
