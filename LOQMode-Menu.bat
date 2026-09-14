@echo off
title LOQ Mode Controller
cd /d "%~dp0"
if exist "build\loq-mode.exe" (
    build\loq-mode.exe
) else if exist "loq-mode.exe" (
    loq-mode.exe
) else (
    echo Error: loq-mode.exe not found. Running build\build.bat...
    call build\build.bat
    build\loq-mode.exe
)
