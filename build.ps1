<#
.SYNOPSIS
  Publishes SmbAutoMount and builds the setup.exe installer.
.EXAMPLE
  .\build.ps1                 # x64
  .\build.ps1 -Arch arm64
#>
param(
    [ValidateSet('x64', 'arm64')] [string]$Arch = 'x64',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'src\SmbAutoMount\SmbAutoMount.csproj'
$publish = Join-Path $root "publish\win-$Arch"

[xml]$xml = Get-Content $proj
$version = ($xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "SmbAutoMount $version ($Arch)" -ForegroundColor Cyan

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $proj -c $Configuration -r "win-$Arch" -p:Platform=$Arch -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }

& $iscc "/DAppVersion=$version" "/DArch=$Arch" "/DPublishDir=$publish" (Join-Path $root 'installer\SmbAutoMount.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
Get-ChildItem (Join-Path $root 'dist') -Filter "*$version-$Arch.exe" | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
