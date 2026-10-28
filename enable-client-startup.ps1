param([ValidateSet('check','enable','restore','marketplace')][string]$Action='enable')
$ErrorActionPreference='Stop'
$pythonExe=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
if(-not(Test-Path -LiteralPath $pythonExe)){
    $pythonCommand=Get-Command python -ErrorAction SilentlyContinue
    if(-not $pythonCommand){throw 'Python 3.11+ is needed to safely update this TOML setting. Alternatively set desktop.mcp_startup_authentication = true manually.'}
    $pythonExe=$pythonCommand.Source
}
& $pythonExe -c 'import sys; sys.exit(0 if sys.version_info >= (3,11) else 1)'
if($LASTEXITCODE -ne 0){throw 'Python 3.11+ is required.'}
& $pythonExe (Join-Path $PSScriptRoot 'enable-client-startup.py') --action $Action
if($LASTEXITCODE -ne 0){throw 'Client startup setting update failed.'}
