[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Full', 'Lite')][string] $Flavor,
    [Parameter(Mandatory)][ValidatePattern('^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$')][string] $Version,
    [ValidateSet('stable', 'beta')][string] $Channel = 'stable',
    [string] $ArtifactsRoot = '',
    [switch] $SkipWebBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$applicationId = 'MyGestures'
$applicationExecutable = 'MyGestures.exe'
$fullFlavor = 'Full'
$liteChannelPrefix = 'lite-'
$targetRuntime = 'win-x64'
$assetPlatform = 'windows-x64'
$deltaMode = 'None'
$webViewFramework = 'webview2'
$desktopFramework = 'net8.0-x64-desktop'
$runtimeLibraryName = 'coreclr.dll'
$checksumFileName = 'SHA256SUMS.txt'
$flavorName = $Flavor.ToLowerInvariant()
$selfContained = ($Flavor -eq $fullFlavor).ToString().ToLowerInvariant()
$packageChannel = if ($Flavor -eq $fullFlavor) { $Channel } else { "$liteChannelPrefix$Channel" }
if (-not $ArtifactsRoot) { $ArtifactsRoot = Join-Path $repositoryRoot 'artifacts' }
$ArtifactsRoot = [IO.Path]::GetFullPath($ArtifactsRoot)
$publishDirectory = Join-Path $ArtifactsRoot "publish/$flavorName"
$packageDirectory = Join-Path $ArtifactsRoot "packages/$Version/$flavorName/$Channel"
$releaseDirectory = Join-Path $ArtifactsRoot "release/$Version"
$buildDirectory = Join-Path $ArtifactsRoot "build/$flavorName"
$project = Join-Path $repositoryRoot 'MyGestures/MyGestures.csproj'
$webDirectory = Join-Path $repositoryRoot 'MyGestures/Web'
$iconPath = Join-Path $repositoryRoot 'MyGestures/Assets/mygestures.ico'
if (-not (Test-Path -LiteralPath $iconPath -PathType Leaf)) { throw "Application icon is missing: $iconPath" }

function Invoke-Checked([string] $Command, [string[]] $Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

if (-not $SkipWebBuild) {
    Push-Location $webDirectory
    try {
        Invoke-Checked npm @('ci')
        Invoke-Checked npm @('run', 'build')
    } finally { Pop-Location }
}
if (-not (Test-Path (Join-Path $webDirectory 'dist/index.html') -PathType Leaf)) {
    throw 'Build the Web settings assets before packaging.'
}

# Only clean generated flavor directories, after verifying their absolute paths.
$artifactPrefix = $ArtifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($directory in @($publishDirectory, $packageDirectory)) {
    $resolved = [IO.Path]::GetFullPath($directory)
    if (-not $resolved.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated output escaped the artifact directory: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    Invoke-Checked dotnet @('tool', 'restore', '--tool-manifest', '.config/dotnet-tools.json')
    Invoke-Checked dotnet @('publish', $project, '--configuration', 'Release', '--runtime', $targetRuntime,
        '--self-contained', $selfContained, '--output', $publishDirectory,
        "-p:OutputPath=$buildDirectory/", "-p:Version=$Version", '-p:PublishSingleFile=false')
    foreach ($requiredFile in @($applicationExecutable, 'Web/index.html')) {
        if (-not (Test-Path (Join-Path $publishDirectory $requiredFile) -PathType Leaf)) {
            throw "Published application file is missing: $requiredFile"
        }
    }
    $runtimeBundled = Test-Path (Join-Path $publishDirectory $runtimeLibraryName) -PathType Leaf
    if ($runtimeBundled -ne ($Flavor -eq $fullFlavor)) { throw "$Flavor has the wrong .NET runtime contents." }
    $frameworks = if ($Flavor -eq $fullFlavor) { $webViewFramework } else { "$desktopFramework,$webViewFramework" }
    Invoke-Checked dotnet @('tool', 'run', 'vpk', '--', 'pack', '--packId', $applicationId,
        '--packVersion', $Version, '--packDir', $publishDirectory, '--mainExe', $applicationExecutable,
        '--packTitle', $applicationId, '--packAuthors', 'qping', '--runtime', $targetRuntime,
        '--channel', $packageChannel, '--delta', $deltaMode, '--framework', $frameworks,
        '--icon', $iconPath, '--outputDir', $packageDirectory)
} finally { Pop-Location }

if (@(Get-ChildItem -LiteralPath $packageDirectory -Filter '*-delta.nupkg' -File).Count -ne 0) {
    throw 'Delta packages must not be produced.'
}
foreach ($asset in @(
    @{ Source = "$applicationId-$packageChannel-Setup.exe"; Suffix = 'setup.exe' },
    @{ Source = "$applicationId-$packageChannel-Portable.zip"; Suffix = 'portable.zip' }
)) {
    $source = Join-Path $packageDirectory $asset.Source
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Release asset is missing: $source" }
    $destination = Join-Path $releaseDirectory "$applicationId-$Version-$assetPlatform-$flavorName-$($asset.Suffix)"
    Copy-Item -LiteralPath $source -Destination $destination -Force
}
foreach ($updaterName in @(
    "$applicationId-$Version-$packageChannel-full.nupkg",
    "releases.$packageChannel.json"
)) {
    $source = Join-Path $packageDirectory $updaterName
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Updater asset is missing: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $releaseDirectory $updaterName) -Force
}
$assetsManifest = Join-Path $packageDirectory "assets.$packageChannel.json"
if (Test-Path -LiteralPath $assetsManifest -PathType Leaf) {
    Copy-Item -LiteralPath $assetsManifest -Destination (Join-Path $releaseDirectory "assets.$packageChannel.json") -Force
}
Get-ChildItem -LiteralPath $releaseDirectory -File |
    Where-Object Name -ne $checksumFileName | Sort-Object Name |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
    Set-Content -LiteralPath (Join-Path $releaseDirectory $checksumFileName) -Encoding utf8
Write-Host "$Flavor complete packages: $releaseDirectory"
