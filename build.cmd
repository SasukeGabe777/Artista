@echo off
setlocal
call "%~dp0scripts\find-dotnet.cmd" || exit /b 1
"%DOTNET%" build "%~dp0Artista.slnx" -c Release
