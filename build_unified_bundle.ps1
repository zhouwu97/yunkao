$ErrorActionPreference = "Stop"

$yunkaoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$yunkaoPython = Join-Path $yunkaoRoot "venv\Scripts\python.exe"
$yunkaoSpec = Join-Path $yunkaoRoot "yunkao_dev.spec"

if (-not (Test-Path $yunkaoPython)) {
    throw "未找到云考打包 Python: $yunkaoPython"
}
if (-not (Test-Path $yunkaoSpec)) {
    throw "未找到云考打包配置: $yunkaoSpec"
}

Push-Location $yunkaoRoot
try {
    $yunkaoDistDir = Join-Path $yunkaoRoot "dist\yunkao"
    $yunkaoBuildDir = Join-Path $yunkaoRoot "build\yunkao_dev"

    $rootFullPath = [IO.Path]::GetFullPath($yunkaoRoot).TrimEnd('\') + '\'
    foreach ($target in @($yunkaoDistDir, $yunkaoBuildDir)) {
        $targetFullPath = [IO.Path]::GetFullPath($target)
        if (-not $targetFullPath.StartsWith($rootFullPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝清理工作区外路径: $targetFullPath"
        }
    }

    if (Test-Path $yunkaoDistDir) {
        Remove-Item $yunkaoDistDir -Recurse -Force
    }
    if (Test-Path $yunkaoBuildDir) {
        Remove-Item $yunkaoBuildDir -Recurse -Force
    }

    & $yunkaoPython -m PyInstaller `
        --noconfirm `
        --clean `
        --workpath $yunkaoBuildDir `
        --distpath (Join-Path $yunkaoRoot "dist") `
        $yunkaoSpec
    if ($LASTEXITCODE -ne 0) {
        throw "PyInstaller 打包失败，退出码: $LASTEXITCODE"
    }

    $builtYunkaoExe = Join-Path $yunkaoDistDir "yunkao.exe"
    if (-not (Test-Path $builtYunkaoExe)) {
        throw "未找到最新构建的 yunkao.exe: $builtYunkaoExe"
    }

    Write-Host ""
    Write-Host "独立版云考打包完成：" -ForegroundColor Green
    Write-Host "  产出路径: $yunkaoDistDir"
    Write-Host ""
}
finally {
    Pop-Location
}
