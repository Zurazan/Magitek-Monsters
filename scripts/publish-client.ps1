param([switch]$UpdateRepository)

$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$upload = Join-Path $pluginRoot '.artifacts/client'
$record = Get-Content -LiteralPath (Join-Path $upload 'upload-manifest.json') -Raw | ConvertFrom-Json
$repository = 'Zurazan/Magitek-Monsters'
if ($record.status -ne 'ready' -or $record.repository -ne "https://github.com/$repository") { throw 'Unexpected client package.' }
if ($record.version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid version.' }
$zipName = "Magitek Monsters v$($record.version).zip"
if ($record.zipFile -ne $zipName) { throw 'Unexpected ZIP filename.' }
$assetName = $zipName.Replace(' ', '.')
if ($record.assetName -ne $assetName) { throw 'Unexpected release asset filename.' }
$zip = Join-Path $upload $zipName
$icon = Join-Path $upload 'icon.png'
$repoFile = Join-Path $upload 'repo.json'
if ($UpdateRepository) {
    $repoBytes = [IO.File]::ReadAllBytes($repoFile)
    $repoEntries = @([Text.Encoding]::UTF8.GetString($repoBytes) | ConvertFrom-Json)
    if ($repoEntries.Count -ne 1 -or $repoEntries[0].InternalName -ne 'MagitekMonsters' -or $repoEntries[0].AssemblyVersion -ne $record.version) { throw 'Unexpected installer manifest.' }
    foreach ($field in @('DownloadLinkInstall', 'DownloadLinkUpdate', 'DownloadLinkTesting')) {
        if ($repoEntries[0].$field -ne $record.downloadUrl) { throw 'Installer download URL mismatch.' }
    }
}
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $record.sha256) { throw 'ZIP hash mismatch.' }
$tag = 'v' + $record.version
$priorPrompt = $env:GIT_TERMINAL_PROMPT
$priorInteractive = $env:GCM_INTERACTIVE
$headers = $null
$credentialLines = $null
$password = $null
try {
    # Keep credentials in memory and out of logs.
    $env:GIT_TERMINAL_PROMPT = '0'
    $env:GCM_INTERACTIVE = 'never'
    if ($env:GH_TOKEN) { $password = $env:GH_TOKEN }
    elseif ($env:GITHUB_TOKEN) { $password = $env:GITHUB_TOKEN }
    else {
        $credentialLines = "protocol=https`nhost=github.com`n`n" | & git -c credential.interactive=false credential fill 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'GitHub login is unavailable.' }
        $passwordLine = $credentialLines | Where-Object { $_.StartsWith('password=') } | Select-Object -First 1
        if (-not $passwordLine) { throw 'GitHub login is unavailable.' }
        $password = $passwordLine.Substring(9)
    }
    $headers = @{ Authorization = 'Bearer ' + $password; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2026-03-10'; 'User-Agent' = 'MagitekMonsters-Publisher' }
    function GitHub([string]$method, [string]$path, $body = $null, [switch]$AllowMissing) {
        $request = @{ Method = $method; Uri = "https://api.github.com/repos/$repository/$path"; Headers = $headers; MaximumRedirection = 0; TimeoutSec = 30 }
        if ($null -ne $body) { $request.Body = $body | ConvertTo-Json -Depth 10 -Compress; $request.ContentType = 'application/json; charset=utf-8' }
        try { return Invoke-RestMethod @request }
        catch {
            $status = [int]$_.Exception.Response.StatusCode
            if ($AllowMissing -and $status -eq 404) { return $null }
            throw "GitHub $method $path failed (HTTP $status)."
        }
    }
    $repoBefore = GitHub 'GET' 'git/ref/heads/repo'
    $existingManifest = $null
    if ($UpdateRepository) {
        $existingManifest = GitHub 'GET' 'contents/repo.json?ref=repo'
        if ($existingManifest.encoding -ne 'base64') { throw 'Unexpected remote installer encoding.' }
        $remoteBytes = [Convert]::FromBase64String($existingManifest.content)
        $remoteEntries = @([Text.Encoding]::UTF8.GetString($remoteBytes) | ConvertFrom-Json)
        if ($remoteEntries.Count -ne 1 -or $remoteEntries[0].InternalName -ne 'MagitekMonsters' -or $remoteEntries[0].AssemblyVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Unexpected remote installer manifest.' }
        if ([Version]$remoteEntries[0].AssemblyVersion -gt [Version]$record.version) { throw 'The installer already has a newer client. It was not changed.' }
    }
    if ($record.sourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'Publishing needs the exact built source commit.' }
    if ($env:GITHUB_SHA -and $env:GITHUB_SHA -ne $record.sourceCommit) { throw 'Package belongs to a different workflow commit.' }
    $existingIcon = GitHub 'GET' "contents/icon.png?ref=$($record.sourceCommit)"
    $iconBytes = [Convert]::FromBase64String($existingIcon.content)
    if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($iconBytes)) -ne (Get-FileHash -LiteralPath $icon -Algorithm SHA256).Hash) { throw 'Built icon does not match the source commit.' }
    $tagReference = GitHub 'GET' "git/ref/tags/$tag" -AllowMissing
    if ($tagReference) {
        $tagCommit = GitHub 'GET' "commits/$tag"
        if ($tagCommit.sha -ne $record.sourceCommit) { throw 'Release tag belongs to a different source commit.' }
    }
    $release = GitHub 'GET' "releases/tags/$tag" -AllowMissing
    if (-not $release) {
        $releases = GitHub 'GET' 'releases?per_page=100'
        $release = $releases | Where-Object tag_name -eq $tag | Select-Object -First 1
    }
    if (-not $release) {
        $main = $record.sourceCommit
        $release = GitHub 'POST' 'releases' @{ tag_name = $tag; target_commitish = $main; name = $tag; body = ''; draft = $true; prerelease = $true }
        Write-Output "Draft release created: $tag"
    }
    if ($release.target_commitish -ne $record.sourceCommit) { throw 'Release tag belongs to a different source commit.' }
    $releaseId = [long]$release.id
    if ($releaseId -le 0) { throw 'Invalid release ID.' }
    $assets = GitHub 'GET' "releases/$releaseId/assets?per_page=100"
    $asset = $assets | Where-Object name -eq $assetName | Select-Object -First 1
    if ($asset) {
        if ($asset.digest -ne "sha256:$($record.sha256)" -or $asset.size -ne $record.zipBytes) { throw 'The release already contains a different ZIP. It was not replaced.' }
        Write-Output 'Release ZIP already matches.'
    }
    else {
        try {
            $asset = Invoke-RestMethod -Method Post -Uri "https://uploads.github.com/repos/$repository/releases/$releaseId/assets?name=$([Uri]::EscapeDataString($assetName))" -Headers $headers -InFile $zip -ContentType 'application/zip' -MaximumRedirection 0 -TimeoutSec 180
        }
        catch { throw "GitHub ZIP upload failed (HTTP $([int]$_.Exception.Response.StatusCode))." }
        Write-Output 'Release ZIP uploaded.'
    }
    if ($asset.state -ne 'uploaded' -or $asset.name -ne $assetName -or $asset.size -ne $record.zipBytes -or $asset.digest -ne "sha256:$($record.sha256)") { throw 'Release ZIP verification failed.' }
    if ($release.draft) { $release = GitHub 'PATCH' "releases/$releaseId" @{ draft = $false } }
    $published = GitHub 'GET' "releases/tags/$tag"
    if ($published.draft) { throw 'Release is still a draft.' }
    $publishedAssets = GitHub 'GET' "releases/$releaseId/assets?per_page=100"
    $publishedAsset = $publishedAssets | Where-Object name -eq $assetName | Select-Object -First 1
    if (-not $publishedAsset -or $publishedAsset.browser_download_url -ne $record.downloadUrl -or $publishedAsset.size -ne $record.zipBytes -or $publishedAsset.digest -ne "sha256:$($record.sha256)") { throw 'Published ZIP verification failed.' }
    $tagCommit = GitHub 'GET' "commits/$tag"
    if ($tagCommit.sha -ne $record.sourceCommit) { throw 'Published tag does not match the built commit.' }
    $repoAfter = GitHub 'GET' 'git/ref/heads/repo'
    if ($repoBefore.object.sha -ne $repoAfter.object.sha) { throw 'The repo branch changed during publishing.' }
    Write-Output "Published: $($published.html_url)"
    Write-Output "Verified ZIP SHA-256: $($record.sha256)"
    if ($UpdateRepository) {
        $manifestHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($repoBytes))
        $remoteHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($remoteBytes))
        if ($manifestHash -ne $remoteHash) {
            $updated = GitHub 'PUT' 'contents/repo.json' @{
                message = "update client to $($record.version)"
                branch = 'repo'
                sha = $existingManifest.sha
                content = [Convert]::ToBase64String($repoBytes)
            }
            Write-Output "Installer updated: $($updated.commit.sha.Substring(0,7))"
        }
        $verifiedManifest = GitHub 'GET' 'contents/repo.json?ref=repo'
        $verifiedBytes = [Convert]::FromBase64String($verifiedManifest.content)
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($verifiedBytes)) -ne $manifestHash) { throw 'Published installer verification failed.' }
        Write-Output "Verified installer: $($record.repoUrl) / $($record.version)"
    }
    else { Write-Output 'repo.json was not changed.' }
}
finally {
    $headers = $null
    $password = $null
    $passwordLine = $null
    $credentialLines = $null
    $env:GIT_TERMINAL_PROMPT = $priorPrompt
    $env:GCM_INTERACTIVE = $priorInteractive
}
