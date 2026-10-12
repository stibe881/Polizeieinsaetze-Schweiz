#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"
#include "SpsController.generated.h"

class SWebBrowser;
class SWeakWidget;
class ASpsCar;
class ASpsClient;

/** Input and the in-game web UI overlay. Game keys are handled here; window keys are forwarded to the web UI. */
UCLASS()
class ASpsController : public APlayerController
{
	GENERATED_BODY()

public:
	ASpsController();
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type Reason) override;
	virtual void SetupInputComponent() override;
	virtual void Tick(float DeltaSeconds) override;

	/** Konsole: "SpsHide Water" / "SpsShow Water" blendet Tile-Teile (Terrain, Roads, BuildingsFar, BuildingsDetail, Water, Furniture, Rail, Trees) aus/ein - zur Fehlersuche. */
	UFUNCTION(Exec) void SpsHide(const FString& Kind);
	UFUNCTION(Exec) void SpsShow(const FString& Kind);
	/** Konsole: "SpsInfo" schreibt Position, Kamera und den Zustand der Kachel unter dem Spieler ins Log. */
	UFUNCTION(Exec) void SpsInfo();

private:
	TSharedPtr<SWebBrowser> Browser;
	TSharedPtr<SWeakWidget> BrowserHolder;
	FDelegateHandle FocusHandle;
	bool bUiFocus = false;

	ASpsClient* Client() const;
	void SetUiFocus(bool bOn);
	void OpenUi(const TCHAR* Window);

	void LookX(float V);
	void LookY(float V);
	void OnEnterExit();
	void OnTalk();
	void OnRadar();
	void OnStop();
	void OnFire();
	void OnCamera();
	void OnAccept();
	void OnDecline();
	void OnQuickSave();
	void OnQuickLoad();
	void OnPause();
	void OnToggleUi();
	void OnComputer() { OpenUi(TEXT("computer")); }
	void OnMap() { OpenUi(TEXT("map")); }
	void OnReport() { OpenUi(TEXT("report")); }
	void OnNotebook() { OpenUi(TEXT("notebook")); }
	void OnCareer() { OpenUi(TEXT("career")); }
	void OnHelp() { OpenUi(TEXT("help")); }
	ASpsCar* FindAimedCar(float& OutDistM) const;
};
