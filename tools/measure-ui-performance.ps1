param(
    [int]$TargetProcessId,
    [ValidateRange(5, 60)][int]$Seconds = 10,
    [switch]$IncludeChildren,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if ($TargetProcessId -eq 0) {
    $candidates = @(Get-Process -Name 'YunKao.App' -ErrorAction SilentlyContinue)
    if ($candidates.Count -ne 1) { throw 'Expected exactly one running YunKao.App process; specify TargetProcessId.' }
    $TargetProcessId = $candidates[0].Id
}
$appProcess = Get-Process -Id $TargetProcessId
if ($appProcess.ProcessName -ne 'YunKao.App') { throw 'TargetProcessId must identify YunKao.App.' }

$measuredProcesses = @($appProcess)
if ($IncludeChildren) {
    $inventory = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId
    $processIds = [Collections.Generic.HashSet[int]]::new()
    [void]$processIds.Add($TargetProcessId)
    do {
        $added = $false
        foreach ($item in $inventory) {
            if ($processIds.Contains([int]$item.ParentProcessId) -and $processIds.Add([int]$item.ProcessId)) {
                $added = $true
            }
        }
    } while ($added)
    $measuredProcesses = @(Get-Process -Id @($processIds) -ErrorAction SilentlyContinue)
}
$lastCpu = @{}
foreach ($measuredProcess in $measuredProcesses) {
    $lastCpu[$measuredProcess.Id] = $measuredProcess.TotalProcessorTime.TotalMilliseconds
}
$startCpu = ($lastCpu.Values | Measure-Object -Sum).Sum
$timer = [Diagnostics.Stopwatch]::StartNew()
$samples = @()
for ($sampleIndex = 0; $sampleIndex -lt $Seconds; $sampleIndex++) {
    Start-Sleep -Seconds 1
    $appProcess.Refresh()
    if ($appProcess.HasExited) { throw 'YunKao.App exited during measurement.' }
    $workingSetBytes = 0L
    $privateBytes = 0L
    $liveProcessCount = 0
    foreach ($measuredProcess in $measuredProcesses) {
        $measuredProcess.Refresh()
        if ($measuredProcess.HasExited) { continue }
        $lastCpu[$measuredProcess.Id] = $measuredProcess.TotalProcessorTime.TotalMilliseconds
        $workingSetBytes += $measuredProcess.WorkingSet64
        $privateBytes += $measuredProcess.PrivateMemorySize64
        $liveProcessCount++
    }
    $samples += [ordered]@{
        elapsedSeconds = [math]::Round($timer.Elapsed.TotalSeconds, 2)
        workingSetMB = [math]::Round($workingSetBytes / 1MB, 2)
        privateMemoryMB = [math]::Round($privateBytes / 1MB, 2)
        processCount = $liveProcessCount
        responding = $appProcess.Responding
    }
}
$appProcess.Refresh()
$report = [ordered]@{
    processId = $TargetProcessId
    executable = $appProcess.Path
    durationSeconds = [math]::Round($timer.Elapsed.TotalSeconds, 2)
    cpuMachinePercent = [math]::Round((($lastCpu.Values | Measure-Object -Sum).Sum - $startCpu) / $timer.Elapsed.TotalMilliseconds / [Environment]::ProcessorCount * 100, 3)
    workingSetMeanMB = [math]::Round(($samples.workingSetMB | Measure-Object -Average).Average, 2)
    workingSetMaxMB = ($samples.workingSetMB | Measure-Object -Maximum).Maximum
    privateMemoryMeanMB = [math]::Round(($samples.privateMemoryMB | Measure-Object -Average).Average, 2)
    allSamplesResponding = -not ($samples.responding -contains $false)
    scope = if ($IncludeChildren) { 'Main WinUI and descendants present at sampling start. Working sets may count shared pages more than once.' } else { 'Main WinUI process only; excludes WebView2 child processes and Worker.' }
    processes = @($measuredProcesses | ForEach-Object { [ordered]@{ id = $_.Id; name = $_.ProcessName } })
    samples = $samples
}
$json = $report | ConvertTo-Json -Depth 5
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutputPath)) | Out-Null
    [IO.File]::WriteAllText($fullOutputPath, $json, [Text.UTF8Encoding]::new($false))
}
$json
