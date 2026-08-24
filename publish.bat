@echo off
rem publishes the single file native build for every architecture.
rem The Visual Studio installer directory is put on PATH because the native linker step shells out to vswhere,
rem and outside a developer prompt it is not on PATH and the link fails with "'vswhere.exe' is not recognized".

setlocal
set PATH=C:\Program Files (x86)\Microsoft Visual Studio\Installer;%PATH%
set PROJECT=%~dp0Tabsh\Tabsh.csproj

echo === win-x86 ===
dotnet publish "%PROJECT%" -c Release -r win-x86 -p:Platform=x86 --nologo || exit /b 1

echo === win-x64 ===
dotnet publish "%PROJECT%" -c Release -r win-x64 -p:Platform=x64 --nologo || exit /b 1

echo === win-arm64 ===
dotnet publish "%PROJECT%" -c Release -r win-arm64 -p:Platform=ARM64 --nologo || exit /b 1

echo.
echo Done. Each executable is under Tabsh\bin\<platform>\Release\net10.0-windows\win-<architecture>\publish.
endlocal
