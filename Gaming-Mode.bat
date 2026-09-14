@echo off
title Switch to Gaming Mode
cd /d "%~dp0"
if exist "build\loq-mode.exe" (
    build\loq-mode.exe gaming
) else if exist "loq-mode.exe" (
    loq-mode.exe gaming
)
echo.
pause
