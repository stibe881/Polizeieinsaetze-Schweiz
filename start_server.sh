#!/bin/sh
# Starts the Swiss Police Simulator game server. Open http://127.0.0.1:47800/ for the web UI sandbox.
cd "$(dirname "$0")" && exec dotnet run --project Tools/SwissServer -c Release "$@"
