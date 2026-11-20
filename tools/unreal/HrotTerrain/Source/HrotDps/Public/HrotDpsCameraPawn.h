// CE-4651 (U-18l) — THE IG's CAMERA: free flight and FOLLOW (watch any unit) as HROT's map does them (the DPS client guide, "Camera
// controllers to build"; the rules in HrotDpsCamera.h, ported from MapCamera3D / FollowCameraDriver / UnitCameraRig).
//
//   free flight   right button held: look around the eye, W/S/A/D along the view, E/Q up / down, the speed from the eye's height (Shift ×4,
//                 Ctrl ×20, both ×100, ramping while held); the wheel steps along the view
//   select        a left click picks the entity under the cursor (its actor → its id)
//   follow        F follows the selected unit on the distance continuum (the wheel walks it: orbit far, over the shoulder near, his eyes
//                 at 0); the right button orbits (at his eyes it offsets the look, springing back); W/A/S/D or Esc hand back to free flight
//                 at the same spot; the unit's end too
//
// Placed every frame from the unit's pose for THIS frame (UHrotDpsWorld::PoseNow — HROT's BeforeDraw, CE-1352: no lag behind a fast
// vehicle). Works under a player controller (input) and without one (Simulate, a test, a render node driven by calls).
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Pawn.h"
#include "HrotDpsCamera.h"
#include "HrotDpsCameraPawn.generated.h"

class UCameraComponent;

UENUM(BlueprintType)
enum class EHrotCameraMode : uint8 { Free, Follow };

UCLASS()
class HROTDPS_API AHrotDpsCamera : public APawn
{
	GENERATED_BODY()

public:
	AHrotDpsCamera();

	/** spawns one in the world (a test or a render node without a placed one) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera", meta = (WorldContext = "WorldContext"))
	static AHrotDpsCamera* SpawnIn(UObject* WorldContext, FVector At, FRotator Rotation);

	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Follow(int32 EntityId, float Distance = 14.f);
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Free();
	/** turns the follow orbit (degrees; yaw counter-clockwise like HROT, pitch up +) — what a right-button drag does */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Orbit(float YawDeg, float PitchDeg);
	/** the follow's place on the continuum (m; 0 = his eyes) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void SetFollowDistance(float Distance) { FollowDistance = Distance; }
	/** a wheel move while following (+ in, − out) — the continuum's step */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Wheel(float Notches);

	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") EHrotCameraMode GetMode() const { return Mode; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") int32 FollowedId() const { return FollowId; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") int32 SelectedId() const { return Selected; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Select(int32 EntityId) { Selected = EntityId; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") float GetFollowDistance() const { return FollowDistance; }
	/** the arm this frame (m) — shorter than the distance when a wall pulled it in */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") float GetArm() const { return Arm; }
	/** where the camera is and looks (Unreal) — what a test or a render node copies */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") FTransform CameraTransform() const;
	/** "free" or "follow <id> at <d> m (orbit|shoulder|eyes), arm <a> m" */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") FString Describe() const;

	UPROPERTY(VisibleAnywhere, Category = "HROT|Camera") TObjectPtr<USceneComponent> Root;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Camera") TObjectPtr<UCameraComponent> Cam;
	/** the camera never goes below this over the ground (m) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Camera") float AboveGround = 0.3f;

	virtual void Tick(float DeltaSeconds) override;
	virtual void BeginPlay() override;

private:
	EHrotCameraMode Mode = EHrotCameraMode::Free;
	int32 FollowId = 0, Selected = 0;
	float FollowDistance = HrotCamera::FollowStart;
	double Yaw = 0, Pitch = -0.35;               // the follow orbit (ENU radians)
	double LookYawOffset = 0, LookPitchOffset = 0;
	float Arm = 0;
	bool bPlaced = false;
	// free flight
	FVector FlyVelocity = FVector::ZeroVector;   // cm/s
	float FlyHeld = 0, WheelTravel = 0, WheelIdle = 10, WheelSign = 0;
	int32 WheelStreak = 0;

	void TickInput(float Dt);
	void PlaceFollow(float Dt);
	void FreeLook(float DYaw, float DPitch);
	float HeightAboveGround(const FVector& At) const;
	/** the first blocking hit's fraction along From → To (Unreal), ignoring the followed actor and its riders; −1 = none */
	double FirstHit(const FVector& From, const FVector& To) const;
	int32 PickUnderCursor() const;
	/** ⚠ measured LIVE (U-18l): at his eyes the camera was INSIDE his head (helmet and gun from within) — his own meshes are hidden
	 *  while the camera is at his eyes (component visibility: the actor's hidden flag is the seat rule's) and shown again after */
	void HideBody(AActor* A, bool bHide);
	TWeakObjectPtr<AActor> HiddenBody;
	TArray<TWeakObjectPtr<UPrimitiveComponent>> HiddenParts;
};
