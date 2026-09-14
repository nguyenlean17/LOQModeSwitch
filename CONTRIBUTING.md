# Contributing to LOQ Mode

Thank you for your interest in improving LOQ Mode! This document provides guidelines for contributing to the project.

## Development Principles
1. **Safety First**: Never perform raw BIOS modifications or write undocumented registry keys.
2. **Minimal Dependencies**: The application must remain lightweight and compile using the native Windows C# compiler (`csc.exe`) without requiring bloated SDKs or heavy runtime dependencies.
3. **Hardware Truthfulness**: Always accurately detect hardware status and reboot requirements (e.g. MUX switch) rather than faking states.
4. **Idempotency**: All operations must be safely re-runnable without corrupting previous baseline configurations.

## Building from Source
Run the included build script:
```cmd
build\build.bat
```
This automatically invokes `csc.exe` from `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.

## Running Verification Tests
Run the automated test suite in PowerShell:
```powershell
powershell -ExecutionPolicy Bypass -File "tests\test_loq_mode.ps1"
```
Ensure all tests pass before submitting a pull request.

## Submitting Pull Requests
1. Fork the repository and create your feature branch: `git checkout -b feature/my-feature`
2. Commit your changes: `git commit -m "Add feature XYZ"`
3. Push to your branch: `git push origin feature/my-feature`
4. Open a Pull Request on GitHub.
