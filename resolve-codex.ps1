function Resolve-MiniQuotaCodex {
    [CmdletBinding()]
    param()
    $quotaCandidates=New-Object 'System.Collections.Generic.List[string]'
    # Prefer a configured CLI, but do not depend on the launching app's PATH.
    $quotaCommands=Get-Command codex.exe,codex.cmd,codex.ps1,codex -CommandType Application,ExternalScript -ErrorAction SilentlyContinue
    foreach($quotaCommand in $quotaCommands){if($quotaCommand.Source -and -not $quotaCandidates.Contains($quotaCommand.Source)){$quotaCandidates.Add($quotaCommand.Source)}}
    $quotaDesktopBin=Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
    if(Test-Path -LiteralPath $quotaDesktopBin){
        $quotaBundled=Get-ChildItem -LiteralPath $quotaDesktopBin -Filter codex.exe -File -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending
        foreach($quotaFile in $quotaBundled){if(-not $quotaCandidates.Contains($quotaFile.FullName)){$quotaCandidates.Add($quotaFile.FullName)}}
    }
    foreach($quotaCandidate in $quotaCandidates){
        try {
            & $quotaCandidate --version *> $null
            if($LASTEXITCODE -ne 0){continue}
            & $quotaCandidate plugin --help *> $null
            if($LASTEXITCODE -eq 0){return $quotaCandidate}
        } catch { continue }
    }
    throw 'A working Codex CLI with plugin support was not found on PATH or in the desktop runtime. Install/start Codex Desktop once, then retry. No plugin configuration was changed.'
}
