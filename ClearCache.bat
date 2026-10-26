@echo off
echo Curatire cache Unity...
echo INCHIDE UNITY INAINTE DE A RULA ACEST SCRIPT!
echo.
pause

set "PROJ=%~dp0"

REM Verifica ca Unity nu ruleaza
tasklist | findstr /i "unity.exe" >nul 2>&1
if %errorlevel% equ 0 (
    echo EROARE: Unity este inca deschis! Inchide-l si incearca din nou.
    pause
    exit /b 1
)

echo Sterg Library\Bee (cache compilare)...
if exist "%PROJ%Library\Bee" (
    rmdir /s /q "%PROJ%Library\Bee"
    echo   OK
)

echo Sterg Library\ScriptAssemblies...
if exist "%PROJ%Library\ScriptAssemblies" (
    rmdir /s /q "%PROJ%Library\ScriptAssemblies"
    echo   OK
)

echo Sterg Temp...
if exist "%PROJ%Temp" (
    rmdir /s /q "%PROJ%Temp"
    echo   OK
)

echo Sterg Library\PackageCache (optional - dureaza mai mult la reimport)...
REM Decommenteaza linia urmatoare daca tot nu merge dupa primele 3:
REM rmdir /s /q "%PROJ%\Library\PackageCache"

echo.
echo Cache sters cu succes!
echo Redeschide proiectul in Unity Hub - va recompila de la zero.
echo Prima deschidere poate dura 2-5 minute.
pause
