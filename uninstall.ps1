param([switch]$RestoreStartupSetting)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'resolve-codex.ps1')
$quotaCodexExe=Resolve-MiniQuotaCodex
Write-Host 'Located a working Codex CLI.'
if($RestoreStartupSetting){ & (Join-Path $PSScriptRoot 'enable-client-startup.ps1') -Action check }
. (Join-Path $PSScriptRoot 'stop-plugin-processes.ps1')
Stop-MiniQuotaCachedProcesses
& $quotaCodexExe plugin remove 'mini-quota@mini-quota-local' --json
if($LASTEXITCODE -ne 0){throw 'Plugin removal did not complete. A remaining cache lock or file permission error may require fully quitting Codex and retrying this uninstaller. No startup setting was restored; do not force-delete other plugins or change directory permissions.'}
. (Join-Path $PSScriptRoot 'manage-marketplace.ps1')
Remove-MiniQuotaMarketplace -CodexExe $quotaCodexExe
if($RestoreStartupSetting){ & (Join-Path $PSScriptRoot 'enable-client-startup.ps1') -Action restore }
else { Write-Host 'Global MCP startup setting preserved. Use enable-client-startup.ps1 -Action restore to restore its saved value.' }
Write-Host 'Fully quit and reopen Codex. Local diagnostics and language preferences are preserved.'
