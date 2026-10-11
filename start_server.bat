@echo off
rem Starts the Swiss Police Simulator game server (needs the .NET 8 SDK or runtime). Open http://127.0.0.1:47800/ for the web UI sandbox.
dotnet run --project Tools\SwissServer -c Release
