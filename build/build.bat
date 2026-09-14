@echo off
setlocal

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found at %CSC%
    exit /b 1
)

set SRC=%~dp0..\src\Program.cs
set OUT=%~dp0loq-mode.exe

echo Compiling LOQ Mode tool...
"%CSC%" /nologo /target:exe /optimize+ /platform:anycpu /r:System.dll /r:System.Core.dll /r:System.Management.dll /out:"%OUT%" "%SRC%"

if %ERRORLEVEL% equ 0 (
    echo [SUCCESS] Built: %OUT%
    exit /b 0
) else (
    echo [FAIL] Compilation failed with error code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)
