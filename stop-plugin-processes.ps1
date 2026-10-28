function Stop-MiniQuotaCachedProcesses {
    [CmdletBinding()]
    param()
    $quotaHome=if($env:CODEX_HOME){$env:CODEX_HOME}else{Join-Path $env:USERPROFILE '.codex'}
    $quotaRoot=[IO.Path]::GetFullPath((Join-Path $quotaHome 'plugins\cache\mini-quota-local\mini-quota')).TrimEnd('\')
    $quotaPrefix=$quotaRoot+'\'
    $quotaProcesses=@(Get-CimInstance Win32_Process -Filter "Name='MiniQuota.exe' OR Name='QuotaBootstrap.exe'")
    $quotaOwned=@($quotaProcesses | Where-Object {
        $_.ExecutablePath -and $_.ExecutablePath.StartsWith($quotaPrefix,[StringComparison]::OrdinalIgnoreCase) -and
        $_.ExecutablePath.Substring($quotaPrefix.Length) -match '^[^\\]+\\bin\\(?:MiniQuota|QuotaBootstrap)\.exe$'
    } | Sort-Object @{Expression={if($_.Name -eq 'QuotaBootstrap.exe'){0}else{1}}})
    $quotaStopped=0
    foreach($quotaEntry in $quotaOwned){
        $quotaProcess=Get-Process -Id $quotaEntry.ProcessId -ErrorAction SilentlyContinue
        if(-not $quotaProcess){continue}
        try {
            # Recheck identity through the process object, not just a reused PID.
            if($quotaProcess.Path -ne $quotaEntry.ExecutablePath -or [Math]::Abs(($quotaProcess.StartTime.ToUniversalTime()-$quotaEntry.CreationDate.ToUniversalTime()).TotalSeconds) -gt 1){continue}
            # The UI owns a quota-query app-server. Stop only this direct child;
            # never stop the desktop app's app-server or any unrelated Codex.
            if($quotaEntry.Name -eq 'MiniQuota.exe'){
                $quotaChildren=Get-CimInstance Win32_Process -Filter "ParentProcessId=$($quotaEntry.ProcessId) AND Name='codex.exe'"
                foreach($quotaChild in $quotaChildren){
                    if($quotaChild.CreationDate -lt $quotaEntry.CreationDate -or $quotaChild.CommandLine -notmatch '(?:^|\s)app-server\s+--listen\s+stdio://(?:\s|$)'){continue}
                    $quotaChildProcess=Get-Process -Id $quotaChild.ProcessId -ErrorAction SilentlyContinue
                    if($quotaChildProcess){try{if($quotaChildProcess.Path -eq $quotaChild.ExecutablePath -and [Math]::Abs(($quotaChildProcess.StartTime.ToUniversalTime()-$quotaChild.CreationDate.ToUniversalTime()).TotalSeconds) -le 1){$quotaChildProcess.Kill();$null=$quotaChildProcess.WaitForExit(3000)}}finally{$quotaChildProcess.Dispose()}}
                }
            }
            $quotaProcess.Kill()
            if(-not $quotaProcess.WaitForExit(3000)){throw 'A Mini Quota process did not exit.'}
            $quotaStopped++
        } finally { $quotaProcess.Dispose() }
    }
    if($quotaStopped -gt 0){Write-Host "Stopped $quotaStopped Mini Quota component process(es) to release the plugin cache."}
}
