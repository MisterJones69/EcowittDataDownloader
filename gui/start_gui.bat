@echo off
rem Starts the built Ecowitt GUI; builds it first if it has not been built yet.
setlocal
set "EXE=%~dp0EcowittGui\bin\publish\EcowittGui.exe"

if not exist "%EXE%" (
    echo GUI noch nicht gebaut - starte Build ...
    call "%~dp0build_gui.bat" nopause
    if errorlevel 1 (
        pause
        exit /b 1
    )
)

rem start detached so this console window closes right away
start "" "%EXE%"
endlocal
