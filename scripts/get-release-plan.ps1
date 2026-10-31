param(
    [string]$Repository = 'Zurazan/Magitek-Monsters',
    [string]$PublishedVersion,
    [switch]$Offline
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$properties = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Client version must have four numeric parts.' }
if ($Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$' -or $Repository.Contains('..')) { throw 'Invalid repository.' }
if ($Offline) {
    if (-not $PublishedVersion) { throw 'Offline checks need a published version.' }
}
else {
    $headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'MagitekMonsters-ReleasePlan' }
    if ($env:GH_TOKEN) { $headers.Authorization = 'Bearer ' + $env:GH_TOKEN }
    try {
        $remote = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/contents/repo.json?ref=repo" -Headers $headers -TimeoutSec 30
        $entries = @([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($remote.content)) | ConvertFrom-Json)
        if ($entries.Count -ne 1 -or $entries[0].InternalName -ne 'MagitekMonsters') { throw 'Unexpected installer manifest.' }
        $PublishedVersion = [string]$entries[0].AssemblyVersion
    }
    catch {
        if ([int]$_.Exception.Response.StatusCode -ne 404) { throw 'Could not check the published client version.' }
        $PublishedVersion = '0.0.0.0'
    }
}
if ($PublishedVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid published version.' }
$release = [Version]$version -gt [Version]$PublishedVersion
if ($env:GITHUB_OUTPUT) {
    @("version=$version", "release=$($release.ToString().ToLowerInvariant())") | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
}
[PSCustomObject]@{ version = $version; publishedVersion = $PublishedVersion; release = $release } | ConvertTo-Json -Compress
