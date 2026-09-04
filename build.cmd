@echo off
setlocal
cd /d "%~dp0"
dotnet test tests\ClaudeAllowHelper.Tests\ClaudeAllowHelper.Tests.csproj -c Release
if errorlevel 1 exit /b 1
dotnet publish src\ClaudeAllowHelper\ClaudeAllowHelper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o artifacts\win-x64
echo.
echo Built: artifacts\win-x64\ClaudeAllowHelper.exe
