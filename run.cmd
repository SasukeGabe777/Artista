@echo off
setlocal
call "%~dp0scripts\find-dotnet.cmd" || exit /b 1
"%DOTNET%" build "%~dp0Artista.slnx" -c Release || exit /b 1
start "" "%~dp0src\Artista.App\bin\Release\net10.0-windows\Artista.exe"
