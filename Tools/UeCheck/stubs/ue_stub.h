// Minimal stand-in for the Unreal Engine API surface used by Unreal/SwissPoliceSim, so the C++ can be syntax- and type-checked with clang without the engine.
// It is NOT the real engine API: names and signatures follow UE 5.4 as far as they are used, nothing is executed.
#pragma once
#include <cmath>
#include <cstdarg>
#include <cstdint>
#include <cstring>
#include <algorithm>
#include <functional>
#include <initializer_list>
#include <map>
#include <memory>
#include <string>
#include <utility>
#include <vector>

#define UCLASS(...)
#define USTRUCT(...)
#define UPROPERTY(...)
#define UFUNCTION(...)
#define GENERATED_BODY()
#define Super AActor
#define TEXT(x) L##x
#define FORCEINLINE inline
#define IMPLEMENT_PRIMARY_GAME_MODULE(a, b, c)
#define SCENE_QUERY_STAT(X) FName(L"" #X)
#define INT32_MIN_STUB (-2147483647 - 1)

typedef int32_t int32; typedef uint8_t uint8; typedef uint16_t uint16; typedef uint32_t uint32; typedef int64_t int64; typedef wchar_t TCHAR; typedef char ANSICHAR; typedef size_t SIZE_T;

struct FName { FName() {} FName(const wchar_t*) {} };
struct FText { static FText FromString(const struct FString&); };
struct FString
{
	std::wstring S;
	FString() {} FString(const wchar_t* c) : S(c ? c : L"") {} FString(const std::wstring& s) : S(s) {}
	const TCHAR* operator*() const { return S.c_str(); }
	bool IsEmpty() const { return S.empty(); }
	bool Contains(const FString& o) const { return S.find(o.S) != std::wstring::npos; }
	int32 Len() const { return (int32)S.size(); } template <class A> void ParseIntoArray(A&, const TCHAR*, bool) const {}
	bool StartsWith(const FString& o) const { return S.rfind(o.S, 0) == 0; }
	FString& operator+=(const FString& o) { S += o.S; return *this; }
	FString& operator+=(const wchar_t* o) { S += o; return *this; }
	bool operator==(const FString& o) const { return S == o.S; } bool operator==(const wchar_t* o) const { return S == o; } bool operator!=(const FString& o) const { return S != o.S; }
	bool operator<(const FString& o) const { return S < o.S; }
	static FString Printf(const TCHAR* fmt, ...) { return FString(fmt); }
};
inline FText FText::FromString(const FString&) { return FText(); }
struct FUTF8ToTCHAR { FUTF8ToTCHAR(const ANSICHAR*, int32) {} operator const TCHAR*() const { return L""; } };
inline FString operator+(FString a, const FString& b) { a.S += b.S; return a; }
inline FString operator+(FString a, const wchar_t* b) { a.S += b; return a; }

template <class T> T&& MoveTemp(T& v) { return static_cast<T&&>(v); }
template <class T> using TFunctionRef = std::function<T>;
template <class T> struct TObjectPtr { T* P = nullptr; TObjectPtr() {} TObjectPtr(T* p) : P(p) {} TObjectPtr& operator=(T* p) { P = p; return *this; } operator T*() const { return P; } T* operator->() const { return P; } T* Get() const { return P; } };
template <class T> using TWeakObjectPtr_Base = T*;
template <class T> struct TWeakObjectPtr { T* P = nullptr; TWeakObjectPtr() {} TWeakObjectPtr(T* p) : P(p) {} bool IsValid() const { return P != nullptr; } T* Get() const { return P; } };
enum class ESPMode { ThreadSafe, NotThreadSafe };
template <class T, ESPMode M = ESPMode::ThreadSafe> struct TSharedPtr : std::shared_ptr<T>
{
	TSharedPtr() {} TSharedPtr(std::nullptr_t) {} TSharedPtr(const std::shared_ptr<T>& p) : std::shared_ptr<T>(p) {}
	template <class U, ESPMode M2> TSharedPtr(const TSharedPtr<U, M2>& o) : std::shared_ptr<T>(o) {}
	bool IsValid() const { return (bool)*this; } T* Get() const { return this->get(); } void Reset() { this->reset(); } TSharedPtr<T, M> ToSharedRef() const { return *this; }
};
template <class T, ESPMode M = ESPMode::ThreadSafe> using TSharedRef = TSharedPtr<T, M>;
template <class T, class... A> TSharedPtr<T, ESPMode::ThreadSafe> MakeShared(A&&... a) { return TSharedPtr<T, ESPMode::ThreadSafe>(std::make_shared<T>(std::forward<A>(a)...)); }

template <class T> struct TArray
{
	std::vector<T> V;
	int32 Num() const { return (int32)V.size(); }
	T& Add(const T& v) { V.push_back(v); return V.back(); } T& Add(T&& v) { V.push_back(std::move(v)); return V.back(); }
	void Append(const T* p, int n) { for (int i = 0; i < n; i++) V.push_back(p[i]); } void Append(std::initializer_list<T> l) { for (auto& x : l) V.push_back(x); } void Append(const TArray<T>& o) { for (auto& x : o.V) V.push_back(x); }
	void AddUninitialized(int32 n) { V.resize(V.size() + n); }
	void SetNum(int32 n) { V.resize(n); } void SetNumUninitialized(int32 n) { V.resize(n); }
	void Reset() { V.clear(); }
	void RemoveAt(int32 i) { V.erase(V.begin() + i); }
	template <class P> int32 RemoveAll(P p) { int32 c = 0; for (int32 i = (int32)V.size() - 1; i >= 0; i--) if (p(V[i])) { V.erase(V.begin() + i); c++; } return c; }
	bool IsValidIndex(int32 i) const { return i >= 0 && i < (int32)V.size(); }
	T* GetData() { return V.data(); } const T* GetData() const { return V.data(); }
	T& operator[](int32 i) { return V[i]; } const T& operator[](int32 i) const { return V[i]; }
	typename std::vector<T>::iterator begin() { return V.begin(); } typename std::vector<T>::iterator end() { return V.end(); }
	typename std::vector<T>::const_iterator begin() const { return V.begin(); } typename std::vector<T>::const_iterator end() const { return V.end(); }
	bool Contains(const T& t) const { return std::find(V.begin(), V.end(), t) != V.end(); }
	TArray() {} TArray(std::initializer_list<T> l) : V(l) {}
};
template <class T> struct TSet
{
	std::vector<T> V; void Add(const T& t) { if (!Contains(t)) V.push_back(t); } bool Contains(const T& t) const { return std::find(V.begin(), V.end(), t) != V.end(); } void Reset() { V.clear(); }
};
template <class K, class V> struct TPair { K Key; V Value; };
template <class K, class V> struct TMap
{
	std::vector<TPair<K, V>> A;
	int32 Num() const { return (int32)A.size(); }
	V* Find(const K& k) { for (auto& p : A) if (p.Key == k) return &p.Value; return nullptr; }
	const V* Find(const K& k) const { for (auto& p : A) if (p.Key == k) return &p.Value; return nullptr; }
	V& Add(const K& k, const V& v) { if (V* f = Find(k)) { *f = v; return *f; } A.push_back({ k, v }); return A.back().Value; }
	bool Contains(const K& k) const { return Find(k) != nullptr; }
	void Remove(const K& k) { for (size_t i = 0; i < A.size(); i++) if (A[i].Key == k) { A.erase(A.begin() + i); return; } }
	void Reset() { A.clear(); }
	auto begin() { return A.begin(); } auto end() { return A.end(); } auto begin() const { return A.begin(); } auto end() const { return A.end(); }
	struct FIter { TMap* M; size_t I; explicit operator bool() const { return I < M->A.size(); } FIter& operator++() { I++; return *this; } const K& Key() const { return M->A[I].Key; } V& Value() const { return M->A[I].Value; } void RemoveCurrent() { M->A.erase(M->A.begin() + I); I--; } };
	FIter CreateIterator() { return FIter{ this, 0 }; }
};
struct FIntPoint { int32 X = 0, Y = 0; FIntPoint() {} FIntPoint(int32 x, int32 y) : X(x), Y(y) {} bool operator==(const FIntPoint& o) const { return X == o.X && Y == o.Y; } };

struct FVector2D { double X = 0, Y = 0; FVector2D() {} FVector2D(double x, double y) : X(x), Y(y) {} };
struct FRotator;
struct FVector
{
	double X = 0, Y = 0, Z = 0; static const FVector ZeroVector;
	FVector() {} FVector(double x, double y, double z) : X(x), Y(y), Z(z) {}
	FVector operator+(const FVector& o) const { return { X + o.X, Y + o.Y, Z + o.Z }; } FVector operator-(const FVector& o) const { return { X - o.X, Y - o.Y, Z - o.Z }; } FVector operator-() const { return { -X, -Y, -Z }; }
	FVector operator*(double s) const { return { X * s, Y * s, Z * s }; } FVector operator/(double s) const { return { X / s, Y / s, Z / s }; } FVector operator/(int s) const { return { X / s, Y / s, Z / s }; }
	FVector& operator+=(const FVector& o) { X += o.X; Y += o.Y; Z += o.Z; return *this; }
	double Size() const { return std::sqrt(X * X + Y * Y + Z * Z); } double Size2D() const { return std::sqrt(X * X + Y * Y); }
	FVector GetSafeNormal() const { double s = Size(); return s > 1e-9 ? *this / s : FVector(); }
	static double Dist(const FVector& a, const FVector& b) { return (a - b).Size(); } static double Dist2D(const FVector& a, const FVector& b) { return std::sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }
	static double DotProduct(const FVector& a, const FVector& b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
	static FVector CrossProduct(const FVector& a, const FVector& b) { return { a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X }; }
	FRotator Rotation() const;
};
inline FVector operator*(double s, const FVector& v) { return v * s; }
inline const FVector FVector::ZeroVector = FVector();
struct FRotator
{
	double Pitch = 0, Yaw = 0, Roll = 0; static const FRotator ZeroRotator;
	FRotator() {} FRotator(double p, double y, double r) : Pitch(p), Yaw(y), Roll(r) {}
	FVector Vector() const; FVector RotateVector(const FVector& v) const;
	static double NormalizeAxis(double a) { while (a > 180) a -= 360; while (a < -180) a += 360; return a; }
};
inline const FRotator FRotator::ZeroRotator = FRotator();
inline FRotator FVector::Rotation() const { return FRotator(); } inline FVector FRotator::Vector() const { return FVector(); } inline FVector FRotator::RotateVector(const FVector& v) const { return v; }
enum class EAxis { X, Y, Z };
struct FRotationMatrix { FRotationMatrix(const FRotator&) {} FVector GetUnitAxis(EAxis) const { return FVector(); } };
struct FColor { uint8 R = 0, G = 0, B = 0, A = 255; FColor() {} FColor(uint8 r, uint8 g, uint8 b, uint8 a = 255) : R(r), G(g), B(b), A(a) {} };
struct FLinearColor { float R, G, B, A; FLinearColor(float r = 0, float g = 0, float b = 0, float a = 1) : R(r), G(g), B(b), A(a) {} FLinearColor operator*(float s) const { return FLinearColor(R * s, G * s, B * s, A); } };

struct FMath
{
	static bool IsFinite(float V) { return std::isfinite(V); }
	template <class T> static T Clamp(T v, T a, T b) { return v < a ? a : (v > b ? b : v); }
	template <class T> static T Min(T a, T b) { return a < b ? a : b; } template <class T> static T Max(T a, T b) { return a > b ? a : b; }
	template <class T> static T Abs(T a) { return a < 0 ? -a : a; } template <class T> static T Sign(T a) { return a < 0 ? -1 : 1; }
	template <class A, class C> static A Lerp(A a, A b, C t) { return a + (b - a) * t; }
	static float Sin(float v) { return std::sin(v); } static float Cos(float v) { return std::cos(v); } static float Tan(float v) { return std::tan(v); } static float Atan2(float a, float b) { return std::atan2(a, b); } static float Acos(float v) { return std::acos(v); }
	static float Fmod(float a, float b) { return std::fmod(a, b); } static double Fmod(double a, double b) { return std::fmod(a, b); }
	static float RadiansToDegrees(float r) { return r * 57.29578f; } static float DegreesToRadians(float d) { return d / 57.29578f; }
	static float FindDeltaAngleDegrees(float a, float b) { return b - a; }
	static float FInterpTo(float c, float t, float dt, float s) { return c + (t - c) * std::min(1.f, dt * s); }
	static FVector VInterpTo(const FVector& c, const FVector& t, float dt, float s) { return c + (t - c) * std::min(1.f, dt * s); }
	static double FloorToDouble(double v) { return std::floor(v); }
	static float FRandRange(float a, float b) { return a; } static double RoundToDouble(double v) { return std::round(v); }
	static float FInterpConstantTo(float c, float t, float dt, float s) { return c; }
	static float Sqrt(float v) { return std::sqrt(v); }
};

struct FMemory { static void Memcpy(void* d, const void* s, size_t n) { std::memcpy(d, s, n); } };
struct FPlatformTime { static double Seconds() { return 0; } };
struct FDelegateHandle {};
template <class... A> struct TMulticastDelegate
{
	template <class U, class M> FDelegateHandle AddUObject(U*, M) { return {}; }
	template <class F> FDelegateHandle AddLambda(F) { return {}; }
	template <class U> void RemoveAll(U*) {} void Remove(FDelegateHandle) {} void Broadcast(A...) {}
};
#define DECLARE_MULTICAST_DELEGATE_OneParam(Name, P) typedef TMulticastDelegate<P> Name;

// ---------------------------------------------------------------- engine core
struct UClass; struct UObject { UClass* GetClass() const; }; struct UClass_Stub {};
struct UInputComponent;
struct UWorld; struct AActor; struct UPrimitiveComponent; struct APlayerController; struct AController;
struct FCollisionQueryParams { FCollisionQueryParams(FName, bool, const AActor* = nullptr) {} void AddIgnoredActor(const AActor*) {} };
enum ECollisionChannel { ECC_Visibility };
struct FHitResult { bool bBlockingHit = false; FVector ImpactPoint; float Distance = 0; AActor* GetActor() const { return nullptr; } };
enum class ECollisionEnabled { NoCollision, QueryAndPhysics }; enum class EComponentMobility { Movable };
struct EEndPlayReason { enum Type { Destroyed }; };
struct FActorTick { bool bCanEverTick = false; };
struct UMaterialInterface : UObject {};
struct UMaterialInstanceDynamic : UMaterialInterface { static UMaterialInstanceDynamic* Create(UMaterialInterface*, UObject*) { return nullptr; } void SetScalarParameterValue(FName, float) {} };
struct USceneComponent : UObject
{
	void SetupAttachment(USceneComponent*, FName = FName()) {} void SetRelativeLocation(const FVector&) {} void SetRelativeRotation(const FRotator&) {} void SetWorldRotation(const FRotator&) {}
	void RegisterComponent() {} void DestroyComponent() {} void SetMobility(EComponentMobility) {} void SetVisibility(bool, bool = false) {} void SetRelativeScale3D(const FVector&) {}
};
struct FProcMeshTangent {};
template <class T> struct StaticClassHolder {};
struct UProceduralMeshComponent : USceneComponent
{
	bool bUseAsyncCooking = false, bUseComplexAsSimpleCollision = false;
	void CreateMeshSection(int32, const TArray<FVector>&, const TArray<int32>&, const TArray<FVector>&, const TArray<FVector2D>&, const TArray<FColor>&, const TArray<FProcMeshTangent>&, bool) {}
	void CreateMeshSection(int32, const TArray<FVector>&, const TArray<int32>&, const TArray<FVector>&, const TArray<FVector2D>&, const TArray<FVector2D>&, const TArray<FVector2D>&, const TArray<FVector2D>&, const TArray<FColor>&, const TArray<FProcMeshTangent>&, bool) {}
	void SetMaterial(int32, UMaterialInterface*) {} UMaterialInterface* GetMaterial(int32) const { return nullptr; }
	void SetCastShadow(bool) {} void SetCollisionEnabled(ECollisionEnabled) {} void SetCollisionProfileName(FName) {} void SetCanEverAffectNavigation(bool) {}
};
struct UStaticMesh : UObject {}; struct USkeletalMesh : UObject {}; struct UAnimationAsset : UObject {};
enum class EAnimationMode { AnimationSingleNode, AnimationBlueprint };
struct UClass : UObject { FString GetName() const { return FString(); } }; inline UClass* UObject::GetClass() const { return nullptr; } struct UAnimInstance : UObject {};
struct FFloatProperty { void SetPropertyValue_InContainer(void*, float) {} }; struct FBoolProperty { void SetPropertyValue_InContainer(void*, bool) {} };
template <class T> T* FindFProperty(UClass*, const TCHAR*) { return nullptr; }
template <class T> UClass* LoadClass(UObject*, const TCHAR*) { return nullptr; }
struct UStaticMeshComponent : USceneComponent { void SetStaticMesh(UStaticMesh*) {} void SetCollisionEnabled(ECollisionEnabled) {} void SetCastShadow(bool) {} void SetRelativeScale3D(const FVector&) {} };
struct USkeletalMeshComponent : USceneComponent { UAnimInstance* GetAnimInstance() const { return nullptr; } void SetAnimInstanceClass(UClass*) {} void SetSkeletalMesh(USkeletalMesh*) {} void SetAnimationMode(EAnimationMode) {} void PlayAnimation(UAnimationAsset*, bool) {} void SetPlayRate(float) {} void SetCollisionEnabled(ECollisionEnabled) {} void SetRelativeScale3D(const FVector&) {} };
enum class ETeleportType { None, TeleportPhysics };
struct FTransform { FTransform() {} FTransform(const FRotator&, const FVector&, const FVector& = FVector()) {} };
struct UInstancedStaticMeshComponent : USceneComponent { void SetStaticMesh(UStaticMesh*) {} void SetCastShadow(bool) {} void SetCollisionEnabled(ECollisionEnabled) {} int32 AddInstance(const FTransform&) { return 0; } int32 GetInstanceCount() const { return 0; } bool BatchUpdateInstancesTransforms(int32, const TArray<FTransform>&, bool, bool, bool) { return true; } void SetMaterial(int32, UMaterialInterface*) {} };
struct UMaterialParameterCollection : UObject {};
struct UKismetMaterialLibrary { static void SetScalarParameterValue(UObject*, UMaterialParameterCollection*, FName, float) {} };
struct UBoxComponent : USceneComponent { void SetBoxExtent(const FVector&) {} void SetCollisionProfileName(FName) {} void SetCanEverAffectNavigation(bool) {} };
struct UCapsuleComponent : USceneComponent { void InitCapsuleSize(float, float) {} void SetCollisionProfileName(FName) {} };
enum EHorizTextAligment { EHTA_Center }; enum EVerticalTextAligment { EVRTA_TextCenter };
struct UTextRenderComponent : USceneComponent { void SetText(const FText&) {} void SetWorldSize(float) {} void SetTextRenderColor(FColor) {} void SetHorizontalAlignment(EHorizTextAligment) {} void SetVerticalAlignment(EVerticalTextAligment) {} void SetCastShadow(bool) {} };
struct UCameraComponent : USceneComponent { void SetFieldOfView(float) {} };
struct USpringArmComponent : USceneComponent
{
	static FName SocketName; float TargetArmLength = 0, CameraLagSpeed = 0, ProbeSize = 0; bool bUsePawnControlRotation = false, bDoCollisionTest = false, bInheritPitch = true, bInheritRoll = true, bInheritYaw = true, bEnableCameraLag = false;
};
inline FName USpringArmComponent::SocketName;
struct UDirectionalLightComponent : USceneComponent { float Intensity = 0.f; void SetLightColor(FLinearColor, bool = true) {} void SetAtmosphereSunLight(bool) {} void SetAtmosphereSunLightIndex(int32) {} void SetIntensity(float) {} void SetLightSourceAngle(float) {} void SetCastShadows(bool) {} };
struct USkyAtmosphereComponent : USceneComponent {};
struct USkyLightComponent : USceneComponent { float Intensity = 0.f; bool bLowerHemisphereIsBlack = true; void SetLowerHemisphereColor(const FLinearColor&) {} bool bRealTimeCapture = false; void SetIntensity(float) {} void RecaptureSky() {} };
struct UExponentialHeightFogComponent : USceneComponent { void SetFogDensity(float) {} void SetVolumetricFog(bool) {} void SetFogInscatteringColor(FLinearColor) {} };
struct UVolumetricCloudComponent : USceneComponent { void SetMaterial(UMaterialInterface*) {} void SetLayerBottomAltitude(float) {} };
enum EAutoExposureMethod { AEM_Histogram };
struct FPostProcessSettings
{
	bool bOverride_AutoExposureMethod = false, bOverride_AutoExposureMinBrightness = false, bOverride_AutoExposureMaxBrightness = false, bOverride_AutoExposureBias = false, bOverride_BloomIntensity = false, bOverride_AmbientOcclusionIntensity = false, bOverride_MotionBlurAmount = false;
	EAutoExposureMethod AutoExposureMethod = AEM_Histogram; float AutoExposureMinBrightness = 0, AutoExposureMaxBrightness = 0, AutoExposureBias = 0, BloomIntensity = 0, AmbientOcclusionIntensity = 0, MotionBlurAmount = 0;
};
struct UPostProcessComponent : USceneComponent { bool bUnbound = false; FPostProcessSettings Settings; };

struct FKey {};
struct UInputComponent : UObject
{
	template <class U, class F> void BindAxisKey(FKey, U*, F) {} template <class U, class F> void BindKey(FKey, int, U*, F) {}
};
namespace EKeys { extern FKey W, A, S, D, F, E, R, G, C, V, M, P, N, K, B, Q, L, H, F1, F2, Insert, F5, F9, Tab, Enter, BackSpace, Escape, LeftShift, SpaceBar, MouseX, MouseY, LeftMouseButton; }
enum { IE_Pressed, IE_Released };
enum class EAutoReceiveInput { Disabled };

struct AActor : UObject
{
	static UClass_Stub* StaticClassStub() { return nullptr; }
	FActorTick PrimaryActorTick; UWorld* GetWorld() const; USceneComponent* RootComponent = nullptr;
	virtual ~AActor() {} virtual void BeginPlay() {} virtual void EndPlay(const EEndPlayReason::Type) {} virtual void Tick(float) {} virtual void SetupPlayerInputComponent(UInputComponent*) {} virtual void SetupInputComponent() {} virtual void StartPlay() {}
	void SetRootComponent(USceneComponent* c) { RootComponent = c; } USceneComponent* GetRootComponent() const { return RootComponent; }
	void SetActorLocation(const FVector&) {} void SetActorLocationAndRotation(const FVector&, const FRotator&) {} void SetActorRotation(const FRotator&) {}
	FVector GetActorLocation() const { return FVector(); } FRotator GetActorRotation() const { return FRotator(); } FVector GetActorRightVector() const { return FVector(); } FVector GetVelocity() const { return FVector(); }
	void Destroy() {} void AddActorWorldOffset(const FVector&, bool, FHitResult* = nullptr, ETeleportType = ETeleportType::None) {}
	void SetActorHiddenInGame(bool) {} void SetActorEnableCollision(bool) {} void SetActorTickEnabled(bool) {}
	template <class T> T* CreateDefaultSubobject(const wchar_t*) { return new T(); }
};
template <class T> T* NewObject(UObject*, const wchar_t*) { return new T(); } template <class T> T* NewObject(UObject*, const FString&) { return new T(); } template <class T> T* NewObject(UObject*, FName) { return new T(); }
template <class T, class U> T* Cast(U* u) { return dynamic_cast<T*>(u); }
struct AController : AActor { FRotator GetControlRotation() const { return FRotator(); } };
struct UCharacterMovementComponent : USceneComponent { float MaxWalkSpeed = 0, JumpZVelocity = 0; bool bOrientRotationToMovement = false; FRotator RotationRate; void SetMovementMode(int) {} };
enum { MOVE_Walking, MOVE_Flying };
struct APawn : AActor { AController* Controller = nullptr; EAutoReceiveInput AutoPossessPlayer = EAutoReceiveInput::Disabled; void AddMovementInput(const FVector&, float) {} };
struct ACharacter : APawn { UCapsuleComponent* GetCapsuleComponent() const { return nullptr; } UCharacterMovementComponent* GetCharacterMovement() const { return nullptr; } void Jump() {} bool bUseControllerRotationYaw = false; };
struct FInputModeGameOnly {}; struct SWidget; struct FInputModeUIOnly { FInputModeUIOnly& SetWidgetToFocus(TSharedPtr<SWidget>) { return *this; } FInputModeUIOnly& SetLockMouseToViewportBehavior(int) { return *this; } };
enum EMouseLockMode { DoNotLock };
namespace EMouseLockMode_NS {}
struct APlayerCameraManager : AActor { FVector GetCameraLocation() const { return FVector(); } FRotator GetCameraRotation() const { return FRotator(); } };
struct APlayerController : AController
{
	bool bShowMouseCursor = false; APlayerCameraManager* PlayerCameraManager = nullptr; UInputComponent* InputComponent = nullptr;
	void Possess(APawn*) {} APawn* GetPawn() const { return nullptr; } template <class M> void SetInputMode(const M&) {} void SetIgnoreLookInput(bool) {} void SetIgnoreMoveInput(bool) {}
	bool IsInputKeyDown(FKey) const { return false; } void AddYawInput(float) {} void AddPitchInput(float) {}
};
struct AGameModeBase : AActor { UClass_Stub* DefaultPawnClass = nullptr; UClass_Stub* PlayerControllerClass = nullptr; UClass_Stub* HUDClass = nullptr; };
struct UGameViewportClient { template <class W> void AddViewportWidgetContent(W, int) {} template <class W> void RemoveViewportWidgetContent(W) {} };
struct UEngine { UGameViewportClient* GameViewport = nullptr; bool Exec(UWorld*, const TCHAR*) { return true; } }; extern UEngine* GEngine;
struct UWorld : UObject
{
	template <class T> T* SpawnActor(const FVector&, const FRotator&) { return new T(); } APlayerController* GetFirstPlayerController() const { return nullptr; } double GetTimeSeconds() const { return 0; }
	bool LineTraceSingleByChannel(FHitResult&, const FVector&, const FVector&, ECollisionChannel, const FCollisionQueryParams& = FCollisionQueryParams(FName(), false)) const { return false; }
};
inline UWorld* AActor::GetWorld() const { return nullptr; }
template <class T> struct TActorIterator { TActorIterator(UWorld*) {} explicit operator bool() const { return false; } T* operator*() const { return nullptr; } T* operator->() const { return nullptr; } TActorIterator& operator++() { return *this; } };
namespace ConstructorHelpers { template <class T> struct FObjectFinder { FObjectFinder(const wchar_t*) {} bool Succeeded() const { return false; } T* Object = nullptr; }; }
enum { LOAD_NoWarn = 1, LOAD_Quiet = 2 };
template <class T> T* LoadObject(UObject*, const TCHAR*, const TCHAR* = nullptr, int = 0) { return nullptr; }
template <class T> T* LoadObject(UObject*, const FString&, const TCHAR*, int) { return nullptr; }
struct FModuleManager { static FModuleManager& Get(); template <class M> static M& LoadModuleChecked(const wchar_t*) { static M m; return m; } };
struct FDefaultGameModuleImpl {};
struct FConfigCache { bool GetInt(const TCHAR*, const TCHAR*, int32&, const FString&) { return false; } void GetString(const TCHAR*, const TCHAR*, FString&, const FString&) {} void GetArray(const TCHAR*, const TCHAR*, TArray<FString>&, const FString&) {} };
struct FCString { static float Atof(const TCHAR*) { return 0; } }; extern FConfigCache* GConfig; extern FString GGameIni;
struct FCommandLine { static const TCHAR* Get() { return L""; } }; struct FParse { static bool Value(const TCHAR*, const TCHAR*, FString&) { return false; } };
#define StaticClass() StaticClassStub()

// JSON
struct FJsonValue; struct FJsonObject
{
	bool TryGetNumberField(const TCHAR*, double&) const { return false; } bool TryGetStringField(const TCHAR*, FString&) const { return false; } bool TryGetBoolField(const TCHAR*, bool&) const { return false; }
	bool TryGetArrayField(const TCHAR*, const TArray<TSharedPtr<FJsonValue>>*&) const { return false; } bool TryGetObjectField(const TCHAR*, const TSharedPtr<FJsonObject>*&) const { return false; }
	double GetNumberField(const TCHAR*) const { return 0; } FString GetStringField(const TCHAR*) const { return FString(); } const TArray<TSharedPtr<FJsonValue>>& GetArrayField(const TCHAR*) const { static TArray<TSharedPtr<FJsonValue>> a; return a; } bool HasField(const TCHAR*) const { return false; }
};
struct FJsonValue { double AsNumber() const { return 0; } const TArray<TSharedPtr<FJsonValue>>& AsArray() const { static TArray<TSharedPtr<FJsonValue>> a; return a; } TSharedPtr<FJsonObject> AsObject() const { return nullptr; } };
template <class = TCHAR> struct TJsonReader {}; template <class = TCHAR> struct TJsonReaderFactory { static TSharedRef<TJsonReader<>> Create(const FString&) { return MakeShared<TJsonReader<>>(); } };
struct FJsonSerializer { static bool Deserialize(TSharedRef<TJsonReader<>>, TSharedPtr<FJsonObject>&) { return false; } };
// websockets
struct IWebSocket
{
	TMulticastDelegate<> OnConnectedD; TMulticastDelegate<const FString&> OnMessageD; TMulticastDelegate<const void*, SIZE_T, bool> OnBinaryD; TMulticastDelegate<int32, const FString&, bool> OnClosedD; TMulticastDelegate<const FString&> OnErrorD;
	TMulticastDelegate<>& OnConnected() { return OnConnectedD; } TMulticastDelegate<const FString&>& OnMessage() { return OnMessageD; } TMulticastDelegate<const void*, SIZE_T, bool>& OnBinaryMessage() { return OnBinaryD; }
	TMulticastDelegate<int32, const FString&, bool>& OnClosed() { return OnClosedD; } TMulticastDelegate<const FString&>& OnConnectionError() { return OnErrorD; }
	void Connect() {} void Close() {} void Send(const FString&) {} bool IsConnected() { return false; }
};
struct FWebSocketsModule { TSharedRef<IWebSocket> CreateWebSocket(const FString&) { return MakeShared<IWebSocket>(); } };
// slate
struct EVisibility { int V = 0; static const EVisibility Collapsed; static const EVisibility SelfHitTestInvisible; static const EVisibility Visible; static const EVisibility HitTestInvisible; bool operator!=(const EVisibility& O) const { return V != O.V; } bool operator==(const EVisibility& O) const { return V == O.V; } };
inline const EVisibility EVisibility::Visible{3}; inline const EVisibility EVisibility::HitTestInvisible{4}; inline const EVisibility EVisibility::Collapsed{1}; inline const EVisibility EVisibility::SelfHitTestInvisible{2};
struct SWidget { EVisibility GetVisibility() const { return {}; } void SetVisibility(EVisibility) {} }; struct SWeakWidget; struct SWebBrowser : SWidget {};
template <class W> struct SArgs { SArgs& InitialURL(const FString&) { return *this; } SArgs& ShowControls(bool) { return *this; } SArgs& ShowAddressBar(bool) { return *this; } SArgs& ShowErrorMessage(bool) { return *this; } SArgs& SupportsTransparency(bool) { return *this; } SArgs& BackgroundColor(FColor) { return *this; } template <class D> SArgs& OnLoadCompleted(D) { return *this; } template <class D> SArgs& OnLoadError(D) { return *this; } SArgs& PossiblyNullContent(TSharedPtr<SWidget>) { return *this; } };
struct SWeakWidget : SWidget {};
#define SAssignNew(Var, W) MakeSlate<W>(Var) = SArgs<W>()
template <class W> struct SlateAssign { TSharedPtr<W>& V; SlateAssign(TSharedPtr<W>& v) : V(v) { V = MakeShared<W>(); } SArgs<W> operator=(SArgs<W> a) { return a; } };
template <class W> SlateAssign<W> MakeSlate(TSharedPtr<W>& v) { return SlateAssign<W>(v); }

struct FSimpleDelegate { template <class F> static FSimpleDelegate CreateLambda(F) { return {}; } };
enum ELogVerbosityStub { Warning };
struct FLogCategoryStub {}; static FLogCategoryStub LogTemp;
#ifndef UE_LOG
#define UE_LOG(Cat, Verb, Fmt, ...) ((void)0)
#endif

// http
struct IHttpResponse { int32 GetResponseCode() const { return 200; } const TArray<uint8>& GetContent() const { static TArray<uint8> a; return a; } };
struct IHttpRequest;
struct FHttpRequestCompleteDelegate { template <class O, class F> void BindUObject(O*, F) {} };
struct IHttpRequest { void SetURL(const FString&) {} void SetVerb(const FString&) {} FHttpRequestCompleteDelegate& OnProcessRequestComplete() { static FHttpRequestCompleteDelegate d; return d; } bool ProcessRequest() { return true; } };
struct FHttpModule { static FHttpModule& Get() { static FHttpModule m; return m; } TSharedRef<IHttpRequest> CreateRequest() { return MakeShared<IHttpRequest>(); } };

struct FSlateApplication { static bool IsInitialized() { return true; } static FSlateApplication& Get() { static FSlateApplication a; return a; } void SetAllUserFocusToGameViewport() {} };
