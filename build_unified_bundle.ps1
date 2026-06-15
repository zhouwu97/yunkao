$ErrorActionPreference = "Stop"

$yunkaoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$oneclassRoot = Join-Path (Split-Path -Parent $yunkaoRoot) "oneclass\wechat_word_bot_v2\wechat_word_bot"

$yunkaoPython = Join-Path $yunkaoRoot "venv\Scripts\python.exe"
$oneclassPython = Join-Path $oneclassRoot ".venv\Scripts\python.exe"

if (-not (Test-Path $yunkaoPython)) {
    throw "未找到云考打包 Python: $yunkaoPython"
}
if (-not (Test-Path $oneclassPython)) {
    throw "未找到 OneClass 打包 Python: $oneclassPython"
}
if (-not (Test-Path $oneclassRoot)) {
    throw "未找到 OneClass 项目目录: $oneclassRoot"
}

Push-Location $yunkaoRoot
try {
    & $yunkaoPython -m PyInstaller --noconfirm --clean yunkao_dev.spec

    Push-Location $oneclassRoot
    try {
        & $oneclassPython -m PyInstaller `
            --noconfirm `
            --clean `
            --onedir `
            --name oneclass `
            --collect-all rapidocr `
            --collect-all onnxruntime `
            --hidden-import cryptography.hazmat.primitives.asymmetric.ed25519 `
            --add-data "config.yaml;." `
            --add-data "vocab.json;." `
            main.py
    }
    finally {
        Pop-Location
    }

    $bundleTarget = Join-Path $yunkaoRoot "dist\yunkao\_internal\oneclass"
    if (Test-Path $bundleTarget) {
        Remove-Item $bundleTarget -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $bundleTarget | Out-Null
    Copy-Item (Join-Path $oneclassRoot "dist\oneclass\*") $bundleTarget -Recurse -Force

    Write-Host ""
    Write-Host "统一包已准备完成：" -ForegroundColor Green
    Write-Host "  云考入口: $(Join-Path $yunkaoRoot 'dist\yunkao\yunkao.exe')"
    Write-Host "  内置 OneClass 运行时: $bundleTarget"
    Write-Host ""
    Write-Host "用户只需要启动 yunkao.exe；OneClass 作为内置运行时由它自动拉起。"
}
finally {
    Pop-Location
}
