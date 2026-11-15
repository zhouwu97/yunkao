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
    $yunkaoVersion = (
        & $yunkaoPython -c "from config.version import APP_VERSION; print(APP_VERSION)"
    ).Trim()
    if ($LASTEXITCODE -ne 0 -or $yunkaoVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw "无法读取有效的应用版本号: $yunkaoVersion"
    }

    $yunkaoArtifactName = "yunkao-v$yunkaoVersion"
    $yunkaoDistRoot = Join-Path $yunkaoRoot "dist"
    $yunkaoDistDir = Join-Path $yunkaoDistRoot $yunkaoArtifactName
    $yunkaoBuildDir = Join-Path $yunkaoRoot "build\yunkao_dev_v$yunkaoVersion"
    $yunkaoZipPath = Join-Path $yunkaoDistRoot "$yunkaoArtifactName-windows-x64.zip"

    $rootFullPath = [IO.Path]::GetFullPath($yunkaoRoot).TrimEnd('\') + '\'
    foreach ($target in @($yunkaoDistDir, $yunkaoBuildDir, $yunkaoZipPath)) {
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
    if (Test-Path $yunkaoZipPath) {
        Remove-Item $yunkaoZipPath -Force
    }

    & $yunkaoPython -m PyInstaller `
        --noconfirm `
        --clean `
        --workpath $yunkaoBuildDir `
        --distpath $yunkaoDistRoot `
        $yunkaoSpec
    if ($LASTEXITCODE -ne 0) {
        throw "PyInstaller 打包失败，退出码: $LASTEXITCODE"
    }

    $builtYunkaoExe = Join-Path $yunkaoDistDir "$yunkaoArtifactName.exe"
    if (-not (Test-Path $builtYunkaoExe)) {
        throw "未找到最新构建的 yunkao.exe: $builtYunkaoExe"
    }

    Copy-Item `
        (Join-Path $yunkaoRoot "README_使用说明.txt") `
        (Join-Path $yunkaoDistDir "README.txt") `
        -Force

    & tar.exe -a -c -f $yunkaoZipPath -C $yunkaoDistRoot $yunkaoArtifactName
    if ($LASTEXITCODE -ne 0) {
        throw "版本压缩包生成失败，退出码: $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "独立版云考 $yunkaoVersion 打包完成：" -ForegroundColor Green
    Write-Host "  产出路径: $yunkaoDistDir"
    Write-Host "  压缩包: $yunkaoZipPath"
    Write-Host ""
}
finally {
    Pop-Location
}
