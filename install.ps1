$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'resolve-codex.ps1')
$quotaCodexExe=Resolve-MiniQuotaCodex
Write-Host 'Located a working Codex CLI.'
foreach($quotaBinary in @('MiniQuota.exe','QuotaBootstrap.exe')){
    if(-not(Test-Path -LiteralPath (Join-Path $PSScriptRoot "plugins\mini-quota\bin\$quotaBinary"))){throw "Missing binary: $quotaBinary. Build the project first."}
}
& (Join-Path $PSScriptRoot 'enable-client-startup.ps1') -Action check
Write-Host 'This enables GLOBAL MCP startup checks. Other enabled MCP services may start or request authentication.'
Write-Host 'The previous setting will be saved for optional restoration by uninstall.ps1.'
. (Join-Path $PSScriptRoot 'manage-marketplace.ps1')
Register-MiniQuotaMarketplace -CodexExe $quotaCodexExe -Root $PSScriptRoot
. (Join-Path $PSScriptRoot 'stop-plugin-processes.ps1')
Stop-MiniQuotaCachedProcesses
& $quotaCodexExe plugin add 'mini-quota@mini-quota-local' --json
if($LASTEXITCODE -ne 0){throw 'Plugin installation failed.'}
try { & (Join-Path $PSScriptRoot 'enable-client-startup.ps1') -Action enable }
catch {
    Write-Warning 'Plugin is installed but startup configuration failed. Resolve the error and rerun enable-client-startup.ps1; installation is incomplete.'
    throw
}
Write-Host 'Installed native MCP startup integration. Restart Codex using the official entry point to verify.'
