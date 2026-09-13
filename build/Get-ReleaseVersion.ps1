[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('stable', 'beta')][string] $Channel,
    [Parameter(Mandatory)][string] $Repository,
    [string] $SourceTag = '',
    [string] $OutputFile = $env:GITHUB_OUTPUT
)

$ErrorActionPreference = 'Stop'
$semanticVersionPattern = '^(?:v)?(?<version>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))$'
$releaseTitlePattern = '^MyGestures v(?<version>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))(?:\s|$)'
$stableTagPattern = '^release-(?:\d{4}-\d{2}-\d{2}|\d{8})(?:-\d+)?$'
$semanticTagPrefix = 'v'
$releaseHistoryLimit = 100
$stableChannel = 'stable'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function ConvertTo-ReleaseVersion([string] $Text) {
    $match = [regex]::Match($Text.Trim(), $semanticVersionPattern)
    if (-not $match.Success) { $match = [regex]::Match($Text.Trim(), $releaseTitlePattern) }
    if ($match.Success) { return [version]::Parse($match.Groups['version'].Value) }
    return $null
}

if ($SourceTag -and ($Channel -ne $stableChannel -or $SourceTag -notmatch $stableTagPattern)) {
    throw "Stable tags must look like release-2026-09-13 or release-20260913. Got: $SourceTag"
}
$baseline = ConvertTo-ReleaseVersion (Get-Content (Join-Path $repositoryRoot 'version.txt') -Raw)
if ($null -eq $baseline) { throw 'version.txt must contain a semantic version.' }
$versions = @($baseline)
$tags = @(git -C $repositoryRoot tag --list "$semanticTagPrefix*")
if ($LASTEXITCODE -ne 0) { throw 'Could not list git tags.' }
foreach ($tag in $tags) {
    $candidate = ConvertTo-ReleaseVersion $tag
    if ($null -ne $candidate) { $versions += $candidate }
}
$releaseJson = gh release list --repo $Repository --limit $releaseHistoryLimit --json tagName,name,isDraft
if ($LASTEXITCODE -ne 0) { throw 'Could not read GitHub release history.' }
foreach ($release in ($releaseJson | ConvertFrom-Json)) {
    if ($release.isDraft) { continue }
    $candidate = ConvertTo-ReleaseVersion $release.tagName
    if ($null -eq $candidate) { $candidate = ConvertTo-ReleaseVersion $release.name }
    if ($null -ne $candidate) { $versions += $candidate }
}
$highest = $versions | Sort-Object -Descending | Select-Object -First 1
$nextVersion = '{0}.{1}.{2}' -f $highest.Major, $highest.Minor, ($highest.Build + 1)
$tag = if ($SourceTag) { $SourceTag } else { "$semanticTagPrefix$nextVersion" }
$prerelease = $Channel -ne $stableChannel
$label = if ($prerelease) { 'Beta' } else { 'Stable' }
if ($OutputFile) {
    @("version=$nextVersion", "tag=$tag", "channel=$Channel", "label=$label", "prerelease=$($prerelease.ToString().ToLowerInvariant())") |
        Add-Content -LiteralPath $OutputFile -Encoding utf8
}
[pscustomobject]@{ Version = $nextVersion; Tag = $tag; Channel = $Channel; Label = $label; Prerelease = $prerelease }
