$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force -Path (Join-Path $appRoot 'bin') | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll')
$compilerArgs = @('/nologo','/target:winexe','/optimize+','/platform:anycpu',('/out:' + (Join-Path $appRoot 'bin\MiniQuota.exe')))
foreach ($ref in $refs) { $compilerArgs += '/reference:' + (Join-Path $framework $ref) }
$compilerArgs += Join-Path $PSScriptRoot 'UsagePet.cs'
$compilerArgs += Join-Path $PSScriptRoot 'NativeMini.cs'
$compilerArgs += Join-Path $PSScriptRoot 'I18n.cs'
$compilerArgs += Join-Path $PSScriptRoot 'Support.cs'
$compilerArgs += Join-Path $PSScriptRoot 'Data.cs'
& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw '构建失败' }

& (Join-Path $framework 'csc.exe') /nologo /target:exe /optimize+ ('/reference:'+(Join-Path $framework 'System.Web.Extensions.dll')) ('/reference:'+(Join-Path $framework 'Microsoft.CSharp.dll')) ('/out:'+(Join-Path $appRoot 'bin\QuotaBootstrap.exe')) (Join-Path $PSScriptRoot 'QuotaBootstrap.cs') (Join-Path $PSScriptRoot 'Support.cs') (Join-Path $PSScriptRoot 'Data.cs')
if ($LASTEXITCODE -ne 0) { throw 'MCP bootstrap build failed.' }
