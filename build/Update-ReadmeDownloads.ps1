[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Repository
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$releaseHistoryLimit = 100
$markerStart = '<!-- mygestures-downloads:start -->'
$markerEnd = '<!-- mygestures-downloads:end -->'

function Get-ChannelLinks([bool] $Prerelease) {
    $releases = gh release list --repo $Repository --limit $releaseHistoryLimit --json tagName,name,isPrerelease,isDraft | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Could not read GitHub release history.' }
    $match = @($releases) | Where-Object { -not $_.isDraft -and ([bool]$_.isPrerelease) -eq $Prerelease } | Select-Object -First 1
    if ($null -eq $match) {
        return [pscustomobject]@{
            Version = '—'
            FullInstallerUrl = ''
            FullPortableUrl = ''
            LiteInstallerUrl = ''
            LitePortableUrl = ''
        }
    }

    $details = gh release view $match.tagName --repo $Repository --json assets,tagName,name | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "Could not read GitHub release $($match.tagName)." }
    $fullInstaller = @($details.assets) | Where-Object { $_.name -like '*-windows-x64-full-setup.exe' } | Select-Object -First 1
    $fullPortable = @($details.assets) | Where-Object { $_.name -like '*-windows-x64-full-portable.zip' } | Select-Object -First 1
    $liteInstaller = @($details.assets) | Where-Object { $_.name -like '*-windows-x64-lite-setup.exe' } | Select-Object -First 1
    $litePortable = @($details.assets) | Where-Object { $_.name -like '*-windows-x64-lite-portable.zip' } | Select-Object -First 1
    $installerName = if ($null -eq $fullInstaller) { '' } else { $fullInstaller.name }
    $versionMatch = [regex]::Match("$($details.name) $installerName", '(?<version>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))')
    $version = if ($versionMatch.Success) { $versionMatch.Groups['version'].Value } else { $details.tagName }
    $downloadBase = "https://github.com/$Repository/releases/download/$($details.tagName)"
    return [pscustomobject]@{
        Version = $version
        FullInstallerUrl = if ($null -eq $fullInstaller) { '' } else { "$downloadBase/$($fullInstaller.name)" }
        FullPortableUrl = if ($null -eq $fullPortable) { '' } else { "$downloadBase/$($fullPortable.name)" }
        LiteInstallerUrl = if ($null -eq $liteInstaller) { '' } else { "$downloadBase/$($liteInstaller.name)" }
        LitePortableUrl = if ($null -eq $litePortable) { '' } else { "$downloadBase/$($litePortable.name)" }
    }
}

function Format-Link([string] $Url, [string] $PublishedText, [string] $MissingText) {
    if ([string]::IsNullOrWhiteSpace($Url)) { return $MissingText }
    return "[$PublishedText]($Url)"
}

function Update-DownloadMatrix {
    param(
        [string] $Path,
        [string] $TypeHeader,
        [string] $ChannelHeader,
        [string] $VersionHeader,
        [string] $InstallerHeader,
        [string] $PortableHeader,
        [string] $FullText,
        [string] $LiteText,
        [string] $DownloadText,
        [string] $MissingText,
        $Stable,
        $Beta
    )

    $content = Get-Content -LiteralPath $Path -Raw
    $startIndex = $content.IndexOf($markerStart)
    $endIndex = $content.IndexOf($markerEnd)
    if ($startIndex -lt 0 -or $endIndex -lt 0 -or $endIndex -le $startIndex) {
        throw "Download matrix markers are missing in $Path"
    }

    $newline = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }
    $table = @"
$markerStart
| $TypeHeader | $ChannelHeader | $VersionHeader | $InstallerHeader | $PortableHeader |
| --- | --- | --- | --- | --- |
| **$FullText** | **Stable** | $($Stable.Version) | $(Format-Link $Stable.FullInstallerUrl $DownloadText $MissingText) | $(Format-Link $Stable.FullPortableUrl $DownloadText $MissingText) |
| **$LiteText** | **Stable** | $($Stable.Version) | $(Format-Link $Stable.LiteInstallerUrl $DownloadText $MissingText) | $(Format-Link $Stable.LitePortableUrl $DownloadText $MissingText) |
| **$FullText** | **Beta** | $($Beta.Version) | $(Format-Link $Beta.FullInstallerUrl $DownloadText $MissingText) | $(Format-Link $Beta.FullPortableUrl $DownloadText $MissingText) |
| **$LiteText** | **Beta** | $($Beta.Version) | $(Format-Link $Beta.LiteInstallerUrl $DownloadText $MissingText) | $(Format-Link $Beta.LitePortableUrl $DownloadText $MissingText) |
$markerEnd
"@
    $table = ($table -replace "`r`n", "`n" -replace "`n", $newline).TrimEnd()
    $updated = $content.Substring(0, $startIndex) + $table + $newline + $content.Substring($endIndex + $markerEnd.Length)
    [System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $Path), $updated.TrimEnd() + $newline)
}

$stable = Get-ChannelLinks -Prerelease $false
$beta = Get-ChannelLinks -Prerelease $true
Update-DownloadMatrix -Path (Join-Path $repositoryRoot 'README.md') `
    -TypeHeader 'Type' -ChannelHeader 'Channel' -VersionHeader 'Version' `
    -InstallerHeader 'Installer' -PortableHeader 'Portable' `
    -FullText 'Full' -LiteText 'Lite' -DownloadText 'Download' -MissingText 'Not published yet' `
    -Stable $stable -Beta $beta
Update-DownloadMatrix -Path (Join-Path $repositoryRoot 'README.zh-CN.md') `
    -TypeHeader '类型' -ChannelHeader '通道' -VersionHeader '版本' `
    -InstallerHeader '完整安装包' -PortableHeader '便携版' `
    -FullText '完整版' -LiteText '精简版' -DownloadText '下载' -MissingText '尚未发布' `
    -Stable $stable -Beta $beta
