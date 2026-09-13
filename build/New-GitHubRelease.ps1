[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$')][string] $Version,
    [Parameter(Mandatory)][string] $Tag,
    [Parameter(Mandatory)][ValidateSet('stable', 'beta')][string] $Channel,
    [Parameter(Mandatory)][string] $Repository,
    [string] $Sha = $env:GITHUB_SHA,
    [string] $ArtifactsRoot = ''
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$applicationId = 'MyGestures'
$assetPlatform = 'windows-x64'
$checksumFileName = 'SHA256SUMS.txt'
$label = if ($Channel -eq 'stable') { 'Stable' } else { 'Beta' }
$prerelease = $Channel -ne 'stable'
if (-not $ArtifactsRoot) { $ArtifactsRoot = Join-Path $repositoryRoot 'artifacts' }
$releaseDirectory = Join-Path ([IO.Path]::GetFullPath($ArtifactsRoot)) "release/$Version"
$notesPath = Join-Path ([IO.Path]::GetTempPath()) "mygestures-release-notes-$Version.md"
$downloadBaseUrl = "https://github.com/$Repository/releases/download/$Tag"

$assets = @(
    @{ File = "$applicationId-$Version-$assetPlatform-full-setup.exe"; Display = 'Windows x64 Full Installer' },
    @{ File = "$applicationId-$Version-$assetPlatform-full-portable.zip"; Display = 'Windows x64 Full Portable' },
    @{ File = "$applicationId-$Version-$assetPlatform-lite-setup.exe"; Display = 'Windows x64 Lite Installer' },
    @{ File = "$applicationId-$Version-$assetPlatform-lite-portable.zip"; Display = 'Windows x64 Lite Portable' },
    @{ File = $checksumFileName; Display = 'SHA256 checksums' }
) | ForEach-Object {
    $path = Join-Path $releaseDirectory $_.File
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release asset is missing: $path" }
    [pscustomobject]@{ Path = (Resolve-Path -LiteralPath $path).Path; File = $_.File; Display = $_.Display }
}

$fullInstaller = $assets[0].File
$fullPortable = $assets[1].File
$liteInstaller = $assets[2].File
$litePortable = $assets[3].File
@"
## Download

**[Download Windows x64 Full Installer (Recommended)]($downloadBaseUrl/$fullInstaller)**

[Download Windows x64 Full Portable]($downloadBaseUrl/$fullPortable)

[Download Windows x64 Lite Installer]($downloadBaseUrl/$liteInstaller) — requires .NET 8 Desktop Runtime

[Download Windows x64 Lite Portable]($downloadBaseUrl/$litePortable) — requires .NET 8 Desktop Runtime

Update ring: **$label**

> ``SHA256SUMS.txt`` can be used to verify the downloads.

## What's Changed
"@ | Set-Content -LiteralPath $notesPath -Encoding utf8

Push-Location $repositoryRoot
try {
    if ([string]::IsNullOrWhiteSpace($Sha)) { throw 'A commit SHA is required to create the release tag.' }
    $tagCommit = git rev-list -n 1 $Tag 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tagCommit)) {
        git tag $Tag $Sha
        if ($LASTEXITCODE -ne 0) { throw "Failed to create tag $Tag" }
        git push origin "refs/tags/$Tag"
        if ($LASTEXITCODE -ne 0) { throw "Failed to push tag $Tag" }
    } else {
        $tagCommit = $tagCommit.Trim()
        if ($tagCommit -ne $Sha) { throw "Tag $Tag already points to $tagCommit, expected $Sha." }
        Write-Host "Tag $Tag already points at $Sha"
    }

    $createArgs = @(
        'release', 'create', $Tag,
        '--repo', $Repository,
        '--title', "$applicationId v$Version ($label)",
        '--notes-file', $notesPath,
        '--generate-notes'
    )
    if ($prerelease) { $createArgs += '--prerelease' } else { $createArgs += '--latest' }
    $createArgs += @($assets | ForEach-Object { "$($_.Path)#$($_.Display)" })
    & gh @createArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
    Remove-Item -LiteralPath $notesPath -Force -ErrorAction SilentlyContinue
}
