# Downloads the latest mpv build for x86-64 (any CPU) from zhongfly/mpv-winbuild,
# checks its SHA-256 against the release's sha256.txt, and extracts it to $Dest.
param([Parameter(Mandatory)] [string] $Dest)
$ErrorActionPreference = 'Stop'

$headers = @{ 'User-Agent' = 'aura-windows-ci' }
if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $env:GITHUB_TOKEN" }
$release = Invoke-RestMethod -Headers $headers 'https://api.github.com/repos/zhongfly/mpv-winbuild/releases/latest'

$asset = $release.assets | Where-Object { $_.name -match '^mpv-x86_64-\d{8}-git-[0-9a-f]+\.7z$' } | Select-Object -First 1
$sums = $release.assets | Where-Object { $_.name -eq 'sha256.txt' } | Select-Object -First 1
if (-not $asset -or -not $sums) { throw "mpv asset not found in release $($release.tag_name)" }

$tmp = Join-Path $env:RUNNER_TEMP 'mpv-dl'
New-Item -ItemType Directory -Force $tmp | Out-Null
$archive = Join-Path $tmp $asset.name
Invoke-WebRequest -Headers $headers $asset.browser_download_url -OutFile $archive
$sumText = (Invoke-WebRequest -Headers $headers $sums.browser_download_url).Content
if ($sumText -is [byte[]]) { $sumText = [Text.Encoding]::UTF8.GetString($sumText) }

$expected = ($sumText -split "`n" | Where-Object { $_ -match [regex]::Escape($asset.name) } | Select-Object -First 1) -split '\s+' | Select-Object -First 1
$actual = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
if ($expected.ToLowerInvariant() -ne $actual) { throw "SHA-256 mismatch for $($asset.name): expected $expected, got $actual" }
Write-Host "mpv $($asset.name) verified ($actual)"

New-Item -ItemType Directory -Force $Dest | Out-Null
7z x $archive "-o$Dest" -y | Out-Null
# Keep only what Aura needs.
Get-ChildItem $Dest -Exclude 'mpv.exe', '*.dll', 'mpv.com' | Remove-Item -Recurse -Force
if (-not (Test-Path (Join-Path $Dest 'mpv.exe'))) { throw 'mpv.exe missing after extract' }
Get-ChildItem $Dest | Format-Table Name, Length
