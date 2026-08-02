# Building

## Prerequisites

- Windows 10/11
- .NET SDK **10.0** or later

Check with `dotnet --list-sdks`. If missing, either:

```powershell
winget install Microsoft.DotNet.SDK.10
```

or the no-admin per-user install (what this repo was developed with):

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
./dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
```

The repo's `build.cmd` / `test.cmd` / `run.cmd` find `dotnet` on PATH **or** in `%LOCALAPPDATA%\Microsoft\dotnet` automatically.

## Commands

| Task | Command |
|---|---|
| Build (Release) | `build.cmd` or `dotnet build Artista.slnx -c Release` |
| Unit tests (81) | `test.cmd` or `dotnet test tests\Artista.Tests\Artista.Tests.csproj -c Release` |
| Run | `run.cmd` or `dotnet run --project src\Artista.App -c Release` |
| UI smoke test | `src\Artista.App\bin\Release\net10.0-windows\Artista.exe --uitest <outDir>` |

## Outputs

- App: `src\Artista.App\bin\<Config>\net10.0-windows\Artista.exe`
- The app is fully offline; it writes settings to `%AppData%\Artista\settings.json` and nothing else.

## Publishing a self-contained build (optional)

```cmd
dotnet publish src\Artista.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output under `src\Artista.App\bin\Release\net10.0-windows\win-x64\publish\`.

## Project notes

- All three projects target `net10.0-windows` with `UseWPF=true` (the Core library uses WPF only for WIC image codecs).
- `AllowUnsafeBlocks` is enabled in App for the WriteableBitmap back-buffer copy.
- No external NuGet dependencies besides the xUnit test stack.
