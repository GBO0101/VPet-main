@echo off
setlocal

set "ROOT=%~dp0"
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
set "PROJECT=%ROOT%VPet-Simulator.Windows\VPet-Simulator.Windows.csproj"
set "APP=%ROOT%VPet-Simulator.Windows\bin\x64\Debug\net8.0-windows\win-x64\VPet-Simulator.Windows.exe"

setx VPET_VISION_MODEL llava:7b >nul

if not exist "%DOTNET%" (
    echo .NET SDK was not found at:
    echo %DOTNET%
    echo.
    echo Install .NET SDK or update Start-VPet.cmd with the correct dotnet.exe path.
    pause
    exit /b 1
)

echo Building VPet...
"%DOTNET%" build "%PROJECT%" -c Debug -p:Platform=x64
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo Starting VPet...
start "" "%APP%"

endlocal
