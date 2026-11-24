param(
    [switch]$Capture,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskPublish = Join-Path $taskRoot 'build\liquid-glass-demo'
$taskProject = Join-Path $PSScriptRoot 'LiquidGlassDemo\LiquidGlassDemo.csproj'
& dotnet publish $taskProject -c Release -p:Platform=x64 --self-contained true -o $taskPublish
if ($LASTEXITCODE -ne 0) { throw 'Liquid glass demo build failed.' }

# The desktop shell owns activation, so ms-appx resources belong to this demo.
$taskLink = Join-Path $taskRoot 'build\LiquidGlassDemo.lnk'
$taskShell = New-Object -ComObject WScript.Shell
$taskShortcut = $taskShell.CreateShortcut($taskLink)
$taskShortcut.TargetPath = Join-Path $taskPublish 'LiquidGlassDemo.exe'
$taskShortcut.WorkingDirectory = $taskPublish
$taskShortcut.WindowStyle = 1
$taskShortcut.Arguments = ''
$taskRequest = Join-Path $taskPublish 'capture-request.txt'
if ($Capture) {
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $taskRoot 'build\liquid-glass-demo-captures'
    }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    [IO.File]::WriteAllText($taskRequest, $OutputDirectory)
} elseif (Test-Path -LiteralPath $taskRequest) {
    Remove-Item -LiteralPath $taskRequest
}
$taskShortcut.Save()
$taskStarted = Get-Date
Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + $taskShortcut.TargetPath + '"') -WindowStyle Hidden
Write-Output ('Demo: ' + $taskShortcut.TargetPath)
if ($Capture) {
    $taskResult = Join-Path $OutputDirectory 'motion.json'
    while (((Get-Date) - $taskStarted).TotalSeconds -lt 30) {
        if ((Test-Path -LiteralPath $taskResult) -and (Get-Item -LiteralPath $taskResult).LastWriteTime -gt $taskStarted) {
            foreach ($taskPhase in @('01-rest', '02-pressed', '03-slow', '04-fast', '05-reversal', '06-release', '07-settled')) {
                $taskImage = Join-Path $OutputDirectory ($taskPhase + '.png')
                if (!(Test-Path -LiteralPath $taskImage) -or (Get-Item -LiteralPath $taskImage).LastWriteTime -le $taskStarted) {
                    throw ('Capture missed phase: ' + $taskPhase)
                }
            }
            foreach ($taskSurface in @('native-card', 'native-button')) {
                foreach ($taskPhase in @('rest', 'held', 'reverse', 'release', 'settled', 'reduced')) {
                    $taskImage = Join-Path $OutputDirectory ($taskSurface + '-' + $taskPhase + '.png')
                    if (!(Test-Path -LiteralPath $taskImage) -or (Get-Item -LiteralPath $taskImage).LastWriteTime -le $taskStarted) {
                        throw ('Native capture missed: ' + $taskImage)
                    }
                }
            }
            Write-Output ('Captures: ' + $OutputDirectory)
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw 'Demo did not finish the capture within 30 seconds; check its window and demo-error.log.'
}
