$ErrorActionPreference = "Stop"

$yunkaoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$yunkaoPython = Join-Path $yunkaoRoot "venv\Scripts\python.exe"

if (-not (Test-Path $yunkaoPython)) {
    throw "未找到云考打包 Python: $yunkaoPython"
}

Push-Location $yunkaoRoot
try {
    $yunkaoDistDir = Join-Path $yunkaoRoot "dist\yunkao"
    $yunkaoBuildDir = Join-Path $yunkaoRoot "build\yunkao_dev"
    if (Test-Path $yunkaoDistDir) {
        Remove-Item $yunkaoDistDir -Recurse -Force
    }
    if (Test-Path $yunkaoBuildDir) {
        Remove-Item $yunkaoBuildDir -Recurse -Force
    }

    & $yunkaoPython -m PyInstaller --noconfirm --clean yunkao_dev.spec

    $builtYunkaoExe = Join-Path $yunkaoRoot "build\yunkao_dev\yunkao.exe"
    if (-not (Test-Path $builtYunkaoExe)) {
        # PyInstaller defaults output to dist\yunkao.exe if it's one-file, let's check standard dist as well
        $builtYunkaoExe = Join-Path $yunkaoRoot "dist\yunkao.exe"
        if (-not (Test-Path $builtYunkaoExe)) {
            throw "未找到最新构建的 yunkao.exe"
        }
    }

    if (-not (Test-Path $yunkaoDistDir)) {
        New-Item -ItemType Directory -Force -Path $yunkaoDistDir | Out-Null
    }
    
    # Check if the output is a directory (onedir) or a file (onefile)
    if (Test-Path -PathType Container $builtYunkaoExe) {
        Copy-Item "$builtYunkaoExe\*" $yunkaoDistDir -Recurse -Force
    } else {
        Copy-Item $builtYunkaoExe (Join-Path $yunkaoDistDir "yunkao.exe") -Force
    }

    Write-Host ""
    Write-Host "独立版云考打包完成：" -ForegroundColor Green
    Write-Host "  产出路径: $yunkaoDistDir"
    Write-Host ""
}
finally {
    Pop-Location
}
