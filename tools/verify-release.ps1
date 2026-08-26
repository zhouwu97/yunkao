param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactPath,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion
)

$ErrorActionPreference = "Stop"
$artifact = [IO.Path]::GetFullPath($ArtifactPath)
if (-not (Test-Path -LiteralPath $artifact -PathType Container)) {
    throw "Release artifact directory not found: $artifact"
}

$required = @(
    "YunKao.App.exe",
    "worker\YunKao.Worker.exe",
    "Scripts\yunkao-bridge.js",
    "README.txt",
    "VERSION",
    "release-manifest.json"
)
foreach ($relativePath in $required) {
    $path = Join-Path $artifact $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release artifact missing required file: $relativePath"
    }
}

$version = (Get-Content -Raw -LiteralPath (Join-Path $artifact "VERSION")).Trim()
if ($version -ne $ExpectedVersion) {
    throw "Release version mismatch: expected $ExpectedVersion, got $version"
}

$manifest = Get-Content -Raw -LiteralPath (Join-Path $artifact "release-manifest.json") | ConvertFrom-Json
if ($manifest.version -ne $ExpectedVersion) {
    throw "Release manifest version mismatch: expected $ExpectedVersion, got $($manifest.version)"
}

$workerHash = (Get-FileHash -LiteralPath (Join-Path $artifact "worker\YunKao.Worker.exe") -Algorithm SHA256).Hash
$bridgeHash = (Get-FileHash -LiteralPath (Join-Path $artifact "Scripts\yunkao-bridge.js") -Algorithm SHA256).Hash
if ($workerHash -ne $manifest.workerSha256 -or $bridgeHash -ne $manifest.bridgeSha256) {
    throw "Release manifest checksum verification failed"
}

$bridge = Get-Content -Raw -LiteralPath (Join-Path $artifact "Scripts\yunkao-bridge.js")
if ($bridge -notmatch "YunKaoBridge") {
    throw "Release bridge does not contain the YunKaoBridge entry point"
}

Write-Host "Release integrity verified: $artifact" -ForegroundColor Green
