@echo off
:loop
echo [dev] Building and launching Aura...
dotnet run
echo [dev] App exited. Restarting in 1 second... (Ctrl+C to stop)
timeout /t 1 /nobreak >nul
goto loop
