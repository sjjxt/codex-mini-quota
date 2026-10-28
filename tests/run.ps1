param([string]$BuildDirectory=(Join-Path $env:TEMP 'mini-quota-tests'),[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force -Path $BuildDirectory | Out-Null
$BuildDirectory=(Resolve-Path -LiteralPath $BuildDirectory).Path
if(-not $SkipBuild){ & (Join-Path $root 'plugins\mini-quota\source\build.ps1') }
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$fake=Join-Path $BuildDirectory 'FakeCodex.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe ('/reference:'+(Join-Path $framework 'System.Web.Extensions.dll')) ('/out:'+$fake) (Join-Path $PSScriptRoot 'FakeCodex.cs')
if($LASTEXITCODE -ne 0){throw 'Fake server build failed.'}
$test=Join-Path $BuildDirectory 'Regression.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe ('/reference:'+(Join-Path $root 'plugins\mini-quota\bin\MiniQuota.exe')) ('/reference:'+(Join-Path $framework 'System.Web.Extensions.dll')) ('/reference:'+(Join-Path $framework 'WPF\WindowsBase.dll')) ('/out:'+$test) (Join-Path $PSScriptRoot 'Regression.cs')
if($LASTEXITCODE -ne 0){throw 'Regression test build failed.'}
Copy-Item -LiteralPath (Join-Path $root 'plugins\mini-quota\bin\MiniQuota.exe') -Destination $BuildDirectory -Force
& $test $fake $BuildDirectory
if($LASTEXITCODE -ne 0){throw 'Regression tests failed.'}
$python=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
if(-not(Test-Path -LiteralPath $python)){$python=(Get-Command python -ErrorAction Stop).Source}
$quotaPreviousTemp=$env:TEMP
$quotaPreviousTmp=$env:TMP
try {
    $env:TEMP=$BuildDirectory
    $env:TMP=$BuildDirectory
    & $python -B (Join-Path $PSScriptRoot 'test_startup_config.py') -v
    if($LASTEXITCODE -ne 0){throw 'Startup configuration tests failed.'}
    & $python -B (Join-Path $PSScriptRoot 'test_bootstrap.py') $BuildDirectory
    if($LASTEXITCODE -ne 0){throw 'MCP smoke tests failed.'}
} finally { $env:TEMP=$quotaPreviousTemp; $env:TMP=$quotaPreviousTmp }
