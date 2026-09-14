@echo off
title Switch to Uni Mode
cd /d "%~dp0"
if exist "build\loq-mode.exe" (
    build\loq-mode.exe uni
) else if exist "loq-mode.exe" (
    loq-mode.exe uni
)
echo.
pause
