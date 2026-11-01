param(
    [string]$Repository = 'Zurazan/Magitek-Monsters',
    [string]$SourceCommit = ''
)

$ErrorActionPreference = 'Stop'
if ($Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$' -or $Repository.Contains('..')) { throw 'Invalid repository.' }
if ($SourceCommit -and $SourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'Invalid source commit.' }
$root = Split-Path -Parent $PSScriptRoot
$release = Join-Path $root 'MagitekMonsters/bin/Release'
$output = Join-Path $root '.artifacts/client'
$manifest = Get-Content -LiteralPath (Join-Path $release 'MagitekMonsters.json') -Raw | ConvertFrom-Json -AsHashtable
$version = [string]$manifest.AssemblyVersion
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid client version.' }
foreach ($name in @('MagitekMonsters.dll', 'MagitekMonsters.Contracts.dll')) {
    if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $release $name)).Version.ToString() -ne $version) { throw "Built version mismatch: $name" }
}
$builtAt = [DateTimeOffset]::UtcNow
if ($env:SOURCE_DATE_EPOCH) {
    $epoch = 0L
    if (-not [long]::TryParse($env:SOURCE_DATE_EPOCH, [ref]$epoch) -or $epoch -lt 315532800 -or $epoch -gt 4354819198) { throw 'Invalid source timestamp.' }
    $builtAt = [DateTimeOffset]::FromUnixTimeSeconds($epoch)
}
$zipName = "Magitek Monsters v$version.zip"
$assetName = $zipName.Replace(' ', '.')
$repositoryUrl = "https://github.com/$Repository"
$downloadUrl = "$repositoryUrl/releases/download/v$version/$assetName"
$manifest.RepoUrl = $repositoryUrl
$manifest.IconUrl = "https://raw.githubusercontent.com/$Repository/main/icon.png"
$manifest.AcceptsFeedback = $false
$manifest.Remove('Changelog')
$store = $manifest.Clone()
foreach ($field in @('DownloadLinkInstall', 'DownloadLinkUpdate', 'DownloadLinkTesting')) { $store[$field] = $downloadUrl }
$store.IsHide = $false; $store.IsTestingExclusive = $false; $store.LastUpdate = $builtAt.ToUnixTimeSeconds()
$files = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-PackageFile([string]$source, [string]$name) {
    if ($name.StartsWith('/') -or $name.Contains('..') -or $name.Contains(':') -or $name.Contains('\')) { throw 'Unsafe package path.' }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Missing or linked package file: $name" }
    $files.Add($name, [IO.Path]::GetFullPath($source))
}
foreach ($name in @('MagitekMonsters.dll', 'MagitekMonsters.Contracts.dll', 'SixLabors.ImageSharp.dll', 'System.Security.Cryptography.ProtectedData.dll', 'MagitekMonsters.deps.json', 'images/icon.png', 'runtimes/win/lib/netstandard2.0/System.Security.Cryptography.ProtectedData.dll')) { Add-PackageFile (Join-Path $release $name) $name }
Add-PackageFile (Join-Path $root 'LICENSE') 'LICENSE'
Add-PackageFile (Join-Path $release 'Graphics/asset-manifest.json') 'Graphics/asset-manifest.json'
$assets = Get-Content -LiteralPath $files['Graphics/asset-manifest.json'] -Raw | ConvertFrom-Json
foreach ($asset in $assets.files) {
    if ($asset.path -notmatch '^(items/(gen\d+/)?[^/]+\.png|pokemon/(shiny/)?[^/]+\.png|overrides/items/[^/]+\.png|types/generation-viii/brilliant-diamond-shining-pearl/\d+\.png|licenses/[^/]+\.txt)$') { throw 'Unexpected graphics path.' }
    $name = 'Graphics/' + $asset.path
    $source = Join-Path $release $name
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $asset.sha256) { throw "Asset hash mismatch: $name" }
    Add-PackageFile $source $name
}
Add-PackageFile (Join-Path $root 'MagitekMonsters/licenses/ImageSharp-LICENSE.txt') 'licenses/ImageSharp-LICENSE.txt'
$restore = Get-Content -LiteralPath (Join-Path $root 'MagitekMonsters/obj/project.assets.json') -Raw | ConvertFrom-Json
$packages = $restore.packageFolders.PSObject.Properties.Name | Select-Object -First 1
if (-not $restore.libraries.'SixLabors.ImageSharp/4.1.3' -or @($restore.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^StbImage(?:Write)?Sharp/' }).Count -gt 0) { throw 'Unexpected sprite decoder dependency.' }
Add-PackageFile (Join-Path $packages 'system.io.hashing/8.0.0/LICENSE.TXT') 'licenses/System.IO.Hashing-LICENSE.txt'
Add-PackageFile (Join-Path $packages 'system.io.hashing/8.0.0/THIRD-PARTY-NOTICES.TXT') 'licenses/System.IO.Hashing-NOTICES.txt'
Add-PackageFile (Join-Path $root 'MagitekMonsters/licenses/AspNetCore-LICENSE.txt') 'licenses/AspNetCore-LICENSE.txt'
Add-PackageFile (Join-Path $packages 'system.security.cryptography.protecteddata/4.4.0/LICENSE.TXT') 'licenses/ProtectedData-LICENSE.txt'
Add-PackageFile (Join-Path $packages 'system.security.cryptography.protecteddata/4.4.0/THIRD-PARTY-NOTICES.TXT') 'licenses/ProtectedData-NOTICES.txt'
$dependencies = Get-Content -LiteralPath $files['MagitekMonsters.deps.json'] -Raw | ConvertFrom-Json -AsHashtable
foreach ($target in $dependencies.targets.Values) {
    foreach ($library in $target.Values) {
        if ($library.runtime) {
            foreach ($path in $library.runtime.Keys) {
                $name = [IO.Path]::GetFileName($path)
                if ($name -notmatch '^[A-Za-z0-9_.-]+\.dll$') { throw 'Unexpected runtime dependency.' }
                if (-not $files.ContainsKey($name)) { Add-PackageFile (Join-Path $release $name) $name }
            }
        }
    }
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
if ((Get-Item -LiteralPath $output).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package output cannot be a link.' }
$zip = Join-Path $output $zipName
$temporary = $zip + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
$utf8 = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $files.Keys | Sort-Object) {
        $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $builtAt
        $sourceStream = [IO.File]::OpenRead($files[$name])
        $zipStream = $entry.Open()
        try { $sourceStream.CopyTo($zipStream) }
        finally { $zipStream.Dispose(); $sourceStream.Dispose() }
    }
    $entry = $archive.CreateEntry('MagitekMonsters.json'); $entry.LastWriteTime = $builtAt
    $writer = [IO.StreamWriter]::new($entry.Open(), $utf8)
    try { $writer.Write(($manifest | ConvertTo-Json -Depth 10)) } finally { $writer.Dispose() }
}
finally { $archive.Dispose() }
Move-Item -LiteralPath $temporary -Destination $zip -Force
[IO.File]::WriteAllText((Join-Path $output 'repo.json'), (ConvertTo-Json -InputObject @($store) -Depth 10), $utf8)
Copy-Item -LiteralPath (Join-Path $root 'icon.png') -Destination (Join-Path $output 'icon.png') -Force
$record = @{ kind='magitekmonsters-client-upload'; status='ready'; version=$version; repository=$repositoryUrl; repoBranch='repo'; contentBranch='main'; repoUrl="https://raw.githubusercontent.com/$Repository/repo/repo.json"; downloadUrl=$downloadUrl; apiLevel=$manifest.DalamudApiLevel; builtAt=$builtAt.ToString('O'); zipFile=$zipName; assetName=$assetName; zipEntries=$files.Count+1; zipBytes=(Get-Item -LiteralPath $zip).Length; sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant(); sourceCommit=$SourceCommit }
[IO.File]::WriteAllText((Join-Path $output 'upload-manifest.json'), ($record | ConvertTo-Json), $utf8)
Write-Output "Checked client package: $zip"
