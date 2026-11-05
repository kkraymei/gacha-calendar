param([string]$IsccPath = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content (Join-Path $repo 'src/GachaCalendar.App/GachaCalendar.App.csproj')
$version = [string]($project.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version' }
$output = Join-Path $repo ".release/$version"
# Unique empty staging prevents old output or personal data from being included.
$publish = Join-Path $output ('staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $publish -Force | Out-Null
dotnet publish (Join-Path $repo 'src/GachaCalendar.App') -c Release -r win-x64 --self-contained true -o $publish '-p:DebugType=None' '-p:DebugSymbols=false' '-p:PublishSingleFile=false' '-p:ContinuousIntegrationBuild=true' "-p:PathMap=$repo=/_/src"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $publish
Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination $publish
$forbidden = Get-ChildItem $publish -Recurse -Force | Where-Object { $_.Name -match '^(dist|cache|settings\.json|entries\.json|\.env.*|\.git|source-samples)$|\.(pdb|pfx|p12|pem|key|log)$' }
if ($forbidden) { throw 'Unexpected local data or debug files in publish output' }
$zip = Join-Path $output "GachaCalendar-$version-win-x64-portable.zip"
if (Test-Path -LiteralPath $zip) { throw "Release already exists: $zip" }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
if (!$IsccPath) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) { $IsccPath = $compiler.Source }
}
if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath)) { throw 'Provide -IsccPath pointing to Inno Setup 6 ISCC.exe' }
& $IsccPath "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDirPath=$output" (Join-Path $PSScriptRoot 'windows-installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$artifacts = @($zip, (Join-Path $output "GachaCalendar-$version-win-x64-Setup.exe"))
$artifacts | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))" } | Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Get-Item -LiteralPath $artifacts | Select-Object FullName, Length
