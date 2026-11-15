@echo off
rem Builds the Ecowitt GUI (Release) into EcowittGui\bin\publish.
setlocal
set "PROJECT=%~dp0EcowittGui\EcowittGui.csproj"
set "OUT=%~dp0EcowittGui\bin\publish"

dotnet publish "%PROJECT%" -c Release -o "%OUT%" --nologo
if errorlevel 1 (
    echo.
    echo Build fehlgeschlagen.
    if "%~1"=="" pause
    exit /b 1
)

echo.
echo Build erfolgreich: %OUT%\EcowittGui.exe
endlocal
