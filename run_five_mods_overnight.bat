@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul

cd /d "%~dp0"
set "ROOT=%CD%"
set "TOOL_PROJECT=%ROOT%\RimDevTools\RimDevTools.csproj"
set "TOOL_DLL=%ROOT%\RimDevTools\bin\Release\net10.0\RimDevTools.dll"
set "MOD_LIST=%ROOT%\.tools\test\modlist\overnight-five-mods.xml"
set "LOG_DIR=%ROOT%\.tools\log\overnight-five-mods"
set "PAUSE_ON_EXIT=1"
if /I "%~1"=="--no-pause" set "PAUSE_ON_EXIT=0"

rem Final overnight settings. Set VALIDATION_ONLY=1 and BATCH_SIZE=1 for a short validation run.
set "VALIDATION_ONLY=0"
set "BATCH_SIZE=999"
set "SCOPE=standard"
set "TIMEOUT_SECONDS=3600"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"
for /f %%I in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss"') do set "RUN_STAMP=%%I"
set "MASTER_LOG=%LOG_DIR%\overnight_%RUN_STAMP%.log"
set "SUMMARY_LOG=%LOG_DIR%\overnight_%RUN_STAMP%_summary.txt"
set /a FAILURES=0
set "FINAL_EXIT=0"

call :Log "Five-mod overnight test started."
call :Log "ValidationOnly=%VALIDATION_ONLY% Scope=%SCOPE% BatchSize=%BATCH_SIZE% Timeout=%TIMEOUT_SECONDS%"
call :Log "ModList=%MOD_LIST%"

call :Log "Building RimDevTools once..."
dotnet build "%TOOL_PROJECT%" -c Release >> "%MASTER_LOG%" 2>&1
if errorlevel 1 (
    call :Log "RimDevTools build failed. No game tests were started."
    set "FINAL_EXIT=10"
    goto :ExitScript
)

call :Log "Deploying current LogSyncMod source once..."
dotnet "%TOOL_DLL%" deploy >> "%MASTER_LOG%" 2>&1
if errorlevel 1 (
    call :Log "Deployment failed. No game tests were started."
    set "FINAL_EXIT=11"
    goto :ExitScript
)

if "%VALIDATION_ONLY%"=="1" (
    call :RunTarget "ludeon.rimworld.biotech" "Biotech_HemogenDrain_Validation" "--defname HemogenDrain"
    goto :Finish
)

call :RunTarget "kindseal.ufli" "UFLI" ""
call :RunTarget "ludeon.rimworld.biotech" "Biotech" ""
call :RunTarget "aoba.deadmanswitch.core" "DeadManSwitch" ""
call :RunTarget "hentailoliteam.axolotl" "MoeLotl" ""
call :RunTarget "solaris.ratkinracemod" "Ratkin" ""

:Finish
call :Log "Five-mod test sequence finished. Failures=!FAILURES!"
echo Failures=!FAILURES!>> "%SUMMARY_LOG%"
echo MasterLog=%MASTER_LOG%>> "%SUMMARY_LOG%"
echo AutoTestOutput=%ROOT%\.tools\log\AutoTestOutput>> "%SUMMARY_LOG%"

if !FAILURES! GTR 0 set "FINAL_EXIT=1"
goto :ExitScript

:ExitScript
if "%PAUSE_ON_EXIT%"=="1" (
    echo.
    echo Script finished with ExitCode=!FINAL_EXIT!.
    echo Summary: %SUMMARY_LOG%
    echo Press any key to close this window.
    pause >nul
)
exit /b !FINAL_EXIT!

:RunTarget
set "TARGET_PACKAGE=%~1"
set "TARGET_LABEL=%~2"
set "EXTRA_ARGS=%~3"
for /f %%I in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss"') do set "TARGET_STAMP=%%I"
set "TARGET_LOG=%LOG_DIR%\%TARGET_STAMP%_%TARGET_LABEL%.log"

call :Log "Starting %TARGET_LABEL% (%TARGET_PACKAGE%)"
dotnet "%TOOL_DLL%" test run ^
  --mode auto ^
  --target-package-id "%TARGET_PACKAGE%" ^
  --active-mods-file "%MOD_LIST%" ^
  --scope "%SCOPE%" ^
  --batch-size "%BATCH_SIZE%" ^
  --timeout-seconds "%TIMEOUT_SECONDS%" ^
  --capture-screenshots ^
  --no-isolated-desktop ^
  %EXTRA_ARGS% > "%TARGET_LOG%" 2>&1
set "TARGET_EXIT=!ERRORLEVEL!"

type "%TARGET_LOG%" >> "%MASTER_LOG%"
echo %TARGET_LABEL%^|%TARGET_PACKAGE%^|ExitCode=!TARGET_EXIT!^|Log=%TARGET_LOG%>> "%SUMMARY_LOG%"
if not "!TARGET_EXIT!"=="0" (
    set /a FAILURES+=1
    call :Log "%TARGET_LABEL% failed with ExitCode=!TARGET_EXIT!. Attempting transaction cleanup before continuing."
    dotnet "%TOOL_DLL%" test cleanup >> "%MASTER_LOG%" 2>&1
) else (
    call :Log "%TARGET_LABEL% completed successfully."
)

powershell -NoProfile -Command "Start-Sleep -Seconds 5"
exit /b 0

:Log
for /f "delims=" %%I in ('powershell -NoProfile -Command "Get-Date -Format 'yyyy-MM-dd HH:mm:ss'"') do set "NOW=%%I"
echo [!NOW!] %~1
echo [!NOW!] %~1>> "%MASTER_LOG%"
exit /b 0
