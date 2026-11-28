@echo off
REM Izrada SVITAC.exe (potreban .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0)
cd /d "%~dp0"
where dotnet >nul 2>nul || (echo .NET SDK nije instaliran. Preuzmi .NET 8 SDK pa pokreni ponovno. & pause & exit /b 1)

dotnet publish SVITAC.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -o publish
if errorlevel 1 (echo. & echo Izrada nije uspjela. & pause & exit /b 1)

echo.
echo Gotovo: %~dp0publish\SVITAC.exe
pause
