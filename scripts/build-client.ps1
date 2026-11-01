param([switch]$NoRestore)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -eq 'true' -and -not $env:SIXLABORS_LICENSE_KEY) {
    throw 'Configure the SIXLABORS_LICENSE_KEY repository secret before building.'
}
$root = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $root
try {
    if (-not $NoRestore) {
        dotnet restore MagitekMonsters.slnx --locked-mode -m:1 -nodeReuse:false
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    }
    dotnet build MagitekMonsters/MagitekMonsters.csproj -c Release --no-restore -m:1 -nodeReuse:false -p:MakePluginZip=false -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
}
finally { Pop-Location }
