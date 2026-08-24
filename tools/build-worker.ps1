param(
    [switch]$PackageForApp,
    [switch]$OneFile
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$pythonCommand = "python"
if (-not [string]::IsNullOrWhiteSpace($env:YUNKAO_PYTHON)) {
    $pythonCommand = $env:YUNKAO_PYTHON
}

$specPath = "worker/YunKao.Worker.spec"
if ($OneFile -or $PackageForApp) {
    $specPath = "worker/YunKao.Worker.onefile.spec"
}

Push-Location $projectRoot
try {
    & $pythonCommand -m PyInstaller --noconfirm --clean $specPath
    if ($LASTEXITCODE -ne 0) {
        throw "PyInstaller build failed. Install pyinstaller, beautifulsoup4, python-docx and pywin32 first."
    }

    if ($PackageForApp) {
        $source = Join-Path -Path $projectRoot -ChildPath "dist\YunKao.Worker.exe"
        $target = Join-Path -Path $projectRoot -ChildPath "worker\YunKao.Worker.exe"
        if (-not (Test-Path $source)) { throw "PyInstaller one-file output not found: $source" }
        Copy-Item -LiteralPath $source -Destination $target -Force
        Write-Host "Worker copied to $target for MSIX packaging."
    }
}
finally {
    Pop-Location
}
