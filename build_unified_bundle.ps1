param(
    [switch]$SkipWorker,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$versionPath = Join-Path $projectRoot "VERSION"
$version = (Get-Content -Raw -LiteralPath $versionPath).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid VERSION: $version" }

if (-not $SkipTests) {
    & dotnet test (Join-Path $projectRoot "src\YunKao.Tests\YunKao.Tests.csproj") --configuration Debug --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Core regression tests failed: exit code $LASTEXITCODE" }
    & python -m pytest (Join-Path $projectRoot "tests") -q
    if ($LASTEXITCODE -ne 0) { throw "Worker regression tests failed: exit code $LASTEXITCODE" }
}

$artifactName = "yunkao-desktop-v$version-windows-x64"
$distRoot = Join-Path $projectRoot "dist"
$artifactDir = Join-Path $distRoot $artifactName
$buildRoot = Join-Path $projectRoot "build\$artifactName"
$publishDir = Join-Path $buildRoot "app"
$zipPath = Join-Path $distRoot "$artifactName.zip"

$rootFullPath = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
foreach ($target in @($artifactDir, $buildRoot, $zipPath)) {
    $targetFullPath = [IO.Path]::GetFullPath($target)
    if (-not $targetFullPath.StartsWith($rootFullPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean path outside workspace: $targetFullPath"
    }
}

foreach ($target in @($artifactDir, $buildRoot, $zipPath)) {
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

if (-not $SkipWorker) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $projectRoot "tools\build-worker.ps1") -PackageForApp -OneFile
    if ($LASTEXITCODE -ne 0) { throw "Worker bundle build failed: exit code $LASTEXITCODE" }
}

$appProject = Join-Path $projectRoot "src\YunKao.App\YunKao.App.csproj"
& dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "WinUI publish failed: exit code $LASTEXITCODE" }

$workerPath = Join-Path $projectRoot "worker\YunKao.Worker.exe"
if (-not (Test-Path -LiteralPath $workerPath)) { throw "Worker bundle not found: $workerPath" }
$publishedWorker = Join-Path $publishDir "worker\YunKao.Worker.exe"
if (-not (Test-Path -LiteralPath $publishedWorker)) { throw "Published WinUI app does not contain Worker: $publishedWorker" }
if (-not (Test-Path -LiteralPath (Join-Path $publishDir "Scripts\yunkao-bridge.js"))) { throw "Published WinUI app does not contain Bridge" }

Copy-Item -LiteralPath $publishDir -Destination $artifactDir -Recurse -Force
$readmePath = (Get-ChildItem -LiteralPath $projectRoot -Filter "README_*.txt" | Select-Object -First 1).FullName
if ([string]::IsNullOrWhiteSpace($readmePath)) { throw "Release README not found" }
Copy-Item -LiteralPath $readmePath -Destination (Join-Path $artifactDir "README.txt") -Force
Copy-Item -LiteralPath $versionPath -Destination (Join-Path $artifactDir "VERSION") -Force

$releaseManifest = [ordered]@{
    version = $version
    platform = "windows-x64"
    generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
    workerSha256 = (Get-FileHash -LiteralPath (Join-Path $artifactDir "worker\YunKao.Worker.exe") -Algorithm SHA256).Hash
    bridgeSha256 = (Get-FileHash -LiteralPath (Join-Path $artifactDir "Scripts\yunkao-bridge.js") -Algorithm SHA256).Hash
}
$releaseManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactDir "release-manifest.json") -Encoding UTF8

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $projectRoot "tools\verify-release.ps1") -ArtifactPath $artifactDir -ExpectedVersion $version
if ($LASTEXITCODE -ne 0) { throw "Release integrity verification failed: exit code $LASTEXITCODE" }

& tar.exe -a -c -f $zipPath -C $distRoot $artifactName
if ($LASTEXITCODE -ne 0) { throw "Archive creation failed: exit code $LASTEXITCODE" }

Write-Host ""
Write-Host "YunKao Desktop $version release bundle built" -ForegroundColor Green
Write-Host "Artifact directory: $artifactDir"
Write-Host "Archive: $zipPath"
