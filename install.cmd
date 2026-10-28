@echo off
setlocal EnableExtensions DisableDelayedExpansion
title Codex Mini Quota - Install
pushd "%~dp0"
if errorlevel 1 (
  echo ERROR: Cannot open the extracted project folder.
  pause
  exit /b 2
)
echo Codex Mini Quota - Install
echo Keep this entire folder after extracting the ZIP.
echo.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
set "quotaExit=%errorlevel%"
echo.
if "%quotaExit%"=="0" (
  echo SUCCESS: Installed. Fully quit and reopen Codex using its official icon.
) else (
  echo FAILED: Installation did not finish. Review the error above.
)
popd
pause
exit /b %quotaExit%
