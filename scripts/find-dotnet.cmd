@echo off
:: Locates a .NET installation that actually has an SDK (a runtime-only
:: "C:\Program Files\dotnet" would pass a plain `where dotnet` check).
where dotnet >nul 2>nul
if %errorlevel%==0 (
    for /f "delims=" %%i in ('dotnet --list-sdks 2^>nul') do (
        set "DOTNET=dotnet"
        exit /b 0
    )
)
if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" (
    "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" --list-sdks >nul 2>nul
    if not errorlevel 1 (
        set "DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
        exit /b 0
    )
)
echo .NET SDK not found. Install it from https://dot.net or run:
echo   winget install Microsoft.DotNet.SDK.10
exit /b 1
