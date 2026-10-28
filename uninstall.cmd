@echo off
setlocal EnableExtensions DisableDelayedExpansion
title Codex Mini Quota - Uninstall
pushd "%~dp0"
if errorlevel 1 (
  echo ERROR: Cannot open the extracted project folder.
  pause
  exit /b 2
)
echo Codex Mini Quota - Uninstall
echo Recommended: fully quit Codex before uninstalling.
echo This uninstaller stops only the cached Mini Quota components automatically.
echo.
echo [1] Uninstall only; keep global MCP startup checks.
echo [2] Uninstall and restore the startup setting saved during installation.
echo [0] Cancel.
echo.
choice /C 120 /N /M "Choose 1, 2 or 0: "
set "quotaChoice=%errorlevel%"
if "%quotaChoice%"=="3" (
  popd
  exit /b 0
)
if not "%quotaChoice%"=="1" if not "%quotaChoice%"=="2" (
  echo ERROR: No valid option selected. Nothing was uninstalled.
  popd
  pause
  exit /b 2
)
set "quotaRestore="
if "%quotaChoice%"=="2" set "quotaRestore=-RestoreStartupSetting"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %quotaRestore%
set "quotaExit=%errorlevel%"
echo.
if "%quotaExit%"=="0" (
  echo SUCCESS: Plugin removed. Local diagnostics and language preferences are preserved.
) else (
  echo FAILED: Uninstallation did not finish. Review the error above.
)
popd
pause
exit /b %quotaExit%
