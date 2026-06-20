$ErrorActionPreference = "Stop"

$yunkaoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$oneclassRoot = Join-Path (Split-Path -Parent $yunkaoRoot) "oneclass\wechat_word_bot_v2\wechat_word_bot"

$yunkaoPython = Join-Path $yunkaoRoot "venv\Scripts\python.exe"
$oneclassPython = Join-Path $oneclassRoot ".venv\Scripts\python.exe"
$smallWhisperCacheRoot = Join-Path $env:USERPROFILE ".cache\huggingface\hub\models--Systran--faster-whisper-small.en\snapshots"

if (-not (Test-Path $yunkaoPython)) {
    throw "未找到云考打包 Python: $yunkaoPython"
}
if (-not (Test-Path $oneclassPython)) {
    throw "未找到 OneClass 打包 Python: $oneclassPython"
}
if (-not (Test-Path $oneclassRoot)) {
    throw "未找到 OneClass 项目目录: $oneclassRoot"
}
if (-not (Test-Path $smallWhisperCacheRoot)) {
    throw "未找到本机缓存的 faster-whisper small.en 模型：$smallWhisperCacheRoot"
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
        throw "未找到最新构建的 yunkao.exe: $builtYunkaoExe"
    }

    if (-not (Test-Path $yunkaoDistDir)) {
        New-Item -ItemType Directory -Force -Path $yunkaoDistDir | Out-Null
    }
    Copy-Item $builtYunkaoExe (Join-Path $yunkaoDistDir "yunkao.exe") -Force

    Push-Location $oneclassRoot
    try {
        $oneclassDistDir = Join-Path $oneclassRoot "dist\oneclass"
        $oneclassBuildDir = Join-Path $oneclassRoot "build\oneclass"
        if (Test-Path $oneclassDistDir) {
            Remove-Item $oneclassDistDir -Recurse -Force
        }
        if (Test-Path $oneclassBuildDir) {
            Remove-Item $oneclassBuildDir -Recurse -Force
        }

        $modelSnapshot = Get-ChildItem $smallWhisperCacheRoot -Directory | Select-Object -First 1
        if (-not $modelSnapshot) {
            throw "未找到可用的 small.en 模型快照目录：$smallWhisperCacheRoot"
        }

        $stagedModelDir = Join-Path $oneclassRoot "build_local_models\small.en"
        if (Test-Path $stagedModelDir) {
            Remove-Item $stagedModelDir -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $stagedModelDir | Out-Null

        Get-ChildItem $modelSnapshot.FullName -Force | ForEach-Object {
            $sourcePath = $_.FullName
            if ($_.LinkType -eq "SymbolicLink" -and $_.Target) {
                $targetPath = $_.Target
                if ($targetPath -is [System.Array]) {
                    $targetPath = $targetPath[0]
                }
                $resolved = Resolve-Path (Join-Path $_.DirectoryName $targetPath)
                $sourcePath = $resolved.Path
            }
            Copy-Item $sourcePath (Join-Path $stagedModelDir $_.Name) -Force
        }

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
            --add-data "$stagedModelDir;models\small.en" `
            main.py
    }
    finally {
        Pop-Location
    }

    $legacyBundleTarget = Join-Path $yunkaoDistDir "oneclass"
    if (Test-Path $legacyBundleTarget) {
        Remove-Item $legacyBundleTarget -Recurse -Force
    }

    $bundleTarget = Join-Path $yunkaoDistDir "_internal\oneclass"
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
