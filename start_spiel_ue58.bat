@echo off
rem Startet den Server (eigenes Fenster) und den Unreal-Editor 5.8 mit dem Projekt SwissPoliceSim.
rem Pfade bei Bedarf anpassen: ENGINE = Installationsordner von Unreal 5.8.
set ENGINE=Z:\UE_5.8
set PROJEKT=%~dp0Unreal\SwissPoliceSim\SwissPoliceSim.uproject
start "SwissPoliceSim Server" "%~dp0start_server.bat"
timeout /t 5 /nobreak >nul
start "" "%ENGINE%\Engine\Binaries\Win64\UnrealEditor.exe" "%PROJEKT%"
