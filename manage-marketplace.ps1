function Get-MiniQuotaMarketplace {
    param([string]$CodexExe)
    $quotaErrorPreference=$ErrorActionPreference
    try { $ErrorActionPreference='Continue'; $quotaJson=& $CodexExe plugin marketplace list --json 2>$null; $quotaListExit=$LASTEXITCODE }
    finally { $ErrorActionPreference=$quotaErrorPreference }
    if($quotaListExit -ne 0){
        # Listing also loads missing manifests. Read only this plugin's local
        # source record when that happens; no arbitrary config editing.
        $quotaMetadata=& (Join-Path $PSScriptRoot 'enable-client-startup.ps1') -Action marketplace
        $quotaRecord=($quotaMetadata -join "`n") | ConvertFrom-Json
        if($quotaRecord){@($quotaRecord)}
        return
    }
    $quotaListing=($quotaJson -join "`n") | ConvertFrom-Json
    @($quotaListing.marketplaces | Where-Object { $_.name -eq 'mini-quota-local' })
}
function ConvertTo-MiniQuotaSourcePath {
    param([string]$Path)
    if(-not $Path){throw 'Missing marketplace source path.'}
    if($Path.StartsWith('\\?\UNC\')){$Path='\\'+$Path.Substring(8)}
    elseif($Path.StartsWith('\\?\')){$Path=$Path.Substring(4)}
    [IO.Path]::GetFullPath($Path).TrimEnd('\')
}
function Assert-MiniQuotaOnlyMarketplace {
    param([string]$Root,[switch]$AllowMissing)
    $quotaManifest=Join-Path $Root '.agents\plugins\marketplace.json'
    if(-not(Test-Path -LiteralPath $quotaManifest)){
        if($AllowMissing -and -not(Test-Path -LiteralPath $Root)){return}
        throw 'Cannot identify the existing marketplace as Mini Quota; registration was left unchanged.'
    }
    $quotaData=Get-Content -LiteralPath $quotaManifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $quotaPlugins=@($quotaData.plugins)
    if($quotaData.name -ne 'mini-quota-local' -or $quotaPlugins.Count -ne 1 -or $quotaPlugins[0].name -ne 'mini-quota' -or $quotaPlugins[0].source.source -ne 'local' -or $quotaPlugins[0].source.path -ne './plugins/mini-quota'){
        throw 'The marketplace contains unexpected entries; refusing to change a source that may serve other plugins.'
    }
}
function Register-MiniQuotaMarketplace {
    param([string]$CodexExe,[string]$Root)
    $quotaNew=ConvertTo-MiniQuotaSourcePath $Root
    Assert-MiniQuotaOnlyMarketplace $quotaNew
    $quotaExisting=@(Get-MiniQuotaMarketplace $CodexExe)
    if($quotaExisting.Count -gt 1){throw 'Multiple conflicting Mini Quota marketplace registrations were found.'}
    $quotaOld=$null
    if($quotaExisting.Count -eq 1){
        $quotaOld=ConvertTo-MiniQuotaSourcePath $quotaExisting[0].root
        if(-not $quotaOld.Equals($quotaNew,[StringComparison]::OrdinalIgnoreCase)){
            Assert-MiniQuotaOnlyMarketplace $quotaOld -AllowMissing
            Write-Host 'Updating Mini Quota marketplace to the current extracted folder.'
            & $CodexExe plugin marketplace remove mini-quota-local --json
            if($LASTEXITCODE -ne 0){throw 'Could not remove the old Mini Quota source registration.'}
        }
    }
    & $CodexExe plugin marketplace add $quotaNew --json
    if($LASTEXITCODE -ne 0){
        if($quotaOld -and (Test-Path -LiteralPath $quotaOld) -and -not $quotaOld.Equals($quotaNew,[StringComparison]::OrdinalIgnoreCase)){
            & $CodexExe plugin marketplace add $quotaOld --json
            if($LASTEXITCODE -eq 0){Write-Warning 'New source registration failed; the previous source was restored.'}
        }
        throw 'Marketplace registration did not complete. Resolve the error and rerun this installer.'
    }
}
function Remove-MiniQuotaMarketplace {
    param([string]$CodexExe)
    $quotaExisting=@(Get-MiniQuotaMarketplace $CodexExe)
    if($quotaExisting.Count -eq 0){return}
    if($quotaExisting.Count -ne 1){throw 'Ambiguous Mini Quota marketplace registration.'}
    $quotaOld=ConvertTo-MiniQuotaSourcePath $quotaExisting[0].root
    Assert-MiniQuotaOnlyMarketplace $quotaOld -AllowMissing
    & $CodexExe plugin marketplace remove mini-quota-local --json
    if($LASTEXITCODE -ne 0){throw 'Plugin files were removed, but its marketplace source could not be removed. Rerun the installer to reconcile the source before installing from another folder.'}
}
