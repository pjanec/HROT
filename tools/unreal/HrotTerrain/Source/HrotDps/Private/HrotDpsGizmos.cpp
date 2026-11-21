// CE-4651 (U-18z) — HROT's GIZMOS in the IG: the client's GizmoView (which layers) and the gateway's GizmoFrame (each layer's complete list of
// primitives, SimNetGateway DD-017 §3.7 / DPS-Client-Guide "Gizmos"), drawn every frame through the world's line batcher (DrawDebug*,
// compiled in Development; not Shipping). Shapes: LineStrip / Arrow / Circle (HROT a sphere: Width = radius) / Polygon (a filled triangle,
// z 0 on the wire: put on the ground here) / Box (centre, then extent x, extent y, angle deg) / Text. Icon is reserved (not sent).
// Rules (the guide): no view, no work; one view per seat; the view is VOLATILE ⇒ resent every 2 s (and so after a world boundary).
// ⚠ First cut, unverified live (the gateway says gizmos were never checked live either). Entity cards (DD-024) will be one more kind here.
#include "HrotDpsWorld.h"
#include "HrotDpsSubsystem.h"
#include "HrotDpsClient.h"
#include "DpsTopics.h"

#include "DrawDebugHelpers.h"
#include "Engine/World.h"

void UHrotDpsWorld::OnGizmoFrame(const FHrotDpsSample& S)
{
	const SimNetGateway_Protocol_DdsPlayer_GizmoFrame& F = *static_cast<const SimNetGateway_Protocol_DdsPlayer_GizmoFrame*>(S.Data);
	const FString Layer = UTF8_TO_TCHAR(F.Layer);
	if (!S.bValid || S.bDisposed) { GizmoLayersShown.Remove(Layer); return; }   // a disposed layer = turned off
	TArray<FGizmoPrim>& Out = GizmoLayersShown.FindOrAdd(Layer);
	Out.Reset(F.Primitives._length);
	for (uint32 I = 0; I < F.Primitives._length; I++)
	{
		const SimNetGateway_Protocol_DdsPlayer_GizmoPrimitive& P = F.Primitives._buffer[I];
		FGizmoPrim& G = Out.AddDefaulted_GetRef();
		G.Shape = uint8(P.Shape);
		G.Colour = FColor(uint8(P.ColourRgba >> 24), uint8(P.ColourRgba >> 16), uint8(P.ColourRgba >> 8), uint8(P.ColourRgba));
		G.Width = P.Width;
		if (P.Text[0]) G.Text = UTF8_TO_TCHAR(P.Text);
		G.Points.Reserve(P.Points._length);
		for (uint32 K = 0; K < P.Points._length; K++) G.Points.Add(FVector(P.Points._buffer[K].X, P.Points._buffer[K].Y, P.Points._buffer[K].Z));
	}
	GizmoFrames++;
}

void UHrotDpsWorld::SendGizmoView(double Real)
{
	if (Real < NextGizmoView || !MyClientId) return;
	NextGizmoView = Real + 2.0;
	UHrotDpsSubsystem* Dps = GEngine ? GEngine->GetEngineSubsystem<UHrotDpsSubsystem>() : nullptr;
	if (!Dps || !Dps->IsRunning()) return;
	if (GizmoLayers.Num() == 0 && !bGizmoViewSent) return;   // no view, no work (and nothing to withdraw)
	TArray<SimNetGateway_Protocol_DdsPlayer_NameItem> Names;
	for (const FString& L : GizmoLayers) { SimNetGateway_Protocol_DdsPlayer_NameItem& N = Names.AddZeroed_GetRef(); FCStringAnsi::Strncpy(N.Name, TCHAR_TO_UTF8(*L), sizeof(N.Name)); }
	SimNetGateway_Protocol_DdsPlayer_GizmoView V{};
	V.Layers._length = V.Layers._maximum = uint32(Names.Num()); V.Layers._buffer = Names.GetData(); V.Layers._release = false;
	V.RateHz = 10.f; V.ClientId = MyClientId;
	Dps->Client().Write(TEXT("DPS_GizmoView"), &V);
	bGizmoViewSent = GizmoLayers.Num() > 0;
}

void UHrotDpsWorld::DrawGizmos()
{
	UWorld* W = GetWorld();
	if (!W || GizmoLayersShown.Num() == 0) return;
	const float GizmoLiftCm = 20.f;   // cm: a ground line above the ground, not in it
	for (const TPair<FString, TArray<FGizmoPrim>>& L : GizmoLayersShown)
		for (const FGizmoPrim& G : L.Value)
		{
			if (G.Points.Num() == 0) continue;
			auto U = [&](int32 I) { return EnuMToUnreal(G.Points[I]) + FVector(0, 0, GizmoLiftCm); };
			const float Th = FMath::Clamp(G.Width, 1.f, 20.f);
			switch (G.Shape)
			{
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeLineStrip:
				for (int32 I = 0; I + 1 < G.Points.Num(); I++) DrawDebugLine(W, U(I), U(I + 1), G.Colour, false, -1.f, SDPG_World, Th);
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeArrow:
				if (G.Points.Num() >= 2) DrawDebugDirectionalArrow(W, U(0), U(1), 60.f, G.Colour, false, -1.f, SDPG_World, Th);
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeCircle:
				DrawDebugSphere(W, U(0), FMath::Max(10.f, G.Width * 100.f), 16, G.Colour, false, -1.f, SDPG_World, 1.f);
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapePolygon:
				if (G.Points.Num() >= 3)   // a filled triangle, z 0 on the wire
				{
					FVector A = U(0), B = U(1), C = U(2);
					for (FVector* V : { &A, &B, &C }) V->Z = GizmoLiftCm + OriginCm.Z;
					DrawDebugLine(W, A, B, G.Colour, false, -1.f, SDPG_World, 1.f); DrawDebugLine(W, B, C, G.Colour, false, -1.f, SDPG_World, 1.f);
					DrawDebugLine(W, C, A, G.Colour, false, -1.f, SDPG_World, 1.f);
				}
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeBox:
				if (G.Points.Num() >= 2)   // centre, then (extent x, extent y, angle deg) in metres
				{
					const FVector C = U(0);
					const FVector3d E = G.Points[1];
					const FQuat Q(FVector::UpVector, -FMath::DegreesToRadians(E.Z));   // ENU counter-clockwise -> Unreal (y flipped)
					DrawDebugBox(W, C, FVector(E.X * 100.0, E.Y * 100.0, 10.0), Q, G.Colour, false, -1.f, SDPG_World, Th);
				}
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeText:
				if (!G.Text.IsEmpty()) DrawDebugString(W, U(0), G.Text, nullptr, G.Colour, 0.f, true, 1.f);
				break;
			default: break;
			}
		}
}

void UHrotDpsWorld::SetGizmoLayers(const TArray<FString>& Layers)
{
	GizmoLayers = Layers;
	NextGizmoView = 0;   // say it now
	for (auto It = GizmoLayersShown.CreateIterator(); It; ++It) if (!Layers.Contains(It.Key())) It.RemoveCurrent();
}

FString UHrotDpsWorld::DescribeGizmos() const
{
	int32 N = 0;
	for (const TPair<FString, TArray<FGizmoPrim>>& L : GizmoLayersShown) N += L.Value.Num();
	return FString::Printf(TEXT("gizmos asked [%s], %d layers shown, %d primitives, %d frames"), *FString::Join(GizmoLayers, TEXT(",")), GizmoLayersShown.Num(), N, GizmoFrames);
}
