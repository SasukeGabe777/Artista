@echo off
setlocal
call "%~dp0scripts\find-dotnet.cmd" || exit /b 1
"%DOTNET%" test "%~dp0tests\Artista.Tests\Artista.Tests.csproj" -c Release
