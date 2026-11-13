<#
.SYNOPSIS
  Fetches the Skua interface assemblies the SkuaLink plugin compiles against.
.DESCRIPTION
  Plan A (default): download the official x64 .msi from the Skua release and unpack it
  with "msiexec /a" (administrative install, nothing is installed). The .msi's SHA-256 must match
  the value pinned below for that version BEFORE msiexec runs; a version without a pinned hash or
  a mismatch stops the script. There is no switch to skip the check.
  Plan B (-FromSource): clone the release tag and build the two library projects (no hash pin:
  it trusts the tag on GitHub).
  Nothing downloaded here is committed; the destination is git-ignored.
#>
[CmdletBinding()]
param(
    [string]$Version = '1.4.5.0',
    [string]$Destination = (Join-Path $PSScriptRoot '..\..\plugin\SkuaLink.SkuaBridge\lib\skua'),
    [switch]$FromSource
)

$ErrorActionPreference = 'Stop'

# SHA-256 of the official x64 .msi per Skua version. Computed from the downloaded file on 2026-11-12 and equal to the
# digest GitHub publishes for the release asset (gh api repos/auqw/Skua/releases/tags/1.4.5.0). Add a version only
# after checking both the same way.
$KnownMsiSha256 = @{
    '1.4.5.0' = '51B6DBF67DCCF33D6329F9B8FACECC952575635ED1A4DC37F861C2E67EC0DEEE'
}

$required = @('Skua.Core.Interfaces.dll', 'Skua.Core.Models.dll')
$optional = @('Skua.Core.dll')

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("skua-assemblies-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$Destination = (Resolve-Path $Destination).Path

try {
    if ($FromSource) {
        $src = Join-Path $work 'src'
        git clone --depth 1 --branch $Version https://github.com/auqw/Skua $src
        if ($LASTEXITCODE -ne 0) { throw "git clone of tag $Version failed." }
        # Skua.Core.Interfaces references Skua.Core.Models, so one build produces both.
        dotnet build (Join-Path $src 'Skua.Core.Interfaces') -c Release
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build of Skua.Core.Interfaces failed.' }
        $searchRoot = $src
    }
    else {
        $msi = Join-Path $work "Skua-$Version-Release-x64.msi"
        $url = "https://github.com/auqw/Skua/releases/download/$Version/Skua-$Version-Release-x64.msi"
        if (-not $KnownMsiSha256.ContainsKey($Version)) {
            throw "No pinned SHA-256 for the Skua $Version .msi. Pin it in `$KnownMsiSha256 after checking it, or use -FromSource."
        }
        Invoke-WebRequest -Uri $url -OutFile $msi
        $actual = (Get-FileHash -Algorithm SHA256 -Path $msi).Hash
        if ($actual -ne $KnownMsiSha256[$Version]) {
            throw "The downloaded Skua $Version .msi does not match its pinned SHA-256 (expected $($KnownMsiSha256[$Version]), got $actual). It was not run."
        }
        Write-Host "Skua $Version .msi SHA-256 matches the pinned value."
        $target = Join-Path $work 'extracted'
        New-Item -ItemType Directory -Path $target | Out-Null
        $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/a', "`"$msi`"", '/qn', "TARGETDIR=`"$target`"") -Wait -PassThru
        if ($p.ExitCode -ne 0) { throw "msiexec /a failed with exit code $($p.ExitCode)." }
        $searchRoot = $target
    }

    $missing = @()
    foreach ($name in $required + $optional) {
        $candidates = Get-ChildItem -Path $searchRoot -Recurse -Filter $name -File -ErrorAction SilentlyContinue
        if ($FromSource) {
            # Prefer Release output of the windows target when several copies exist.
            $candidates = $candidates | Where-Object { $_.FullName -match '[\\/]bin[\\/]Release[\\/]' } |
                Sort-Object { $_.FullName -notmatch 'net10\.0-windows' }
        }
        $file = $candidates | Select-Object -First 1
        if (-not $file) {
            if ($required -contains $name) { $missing += $name }
            else { Write-Host "Optional $name not found (not needed unless the plugin proves it)." }
            continue
        }
        Copy-Item -Path $file.FullName -Destination (Join-Path $Destination $name) -Force
        $hash = (Get-FileHash -Algorithm SHA256 -Path $file.FullName).Hash
        $ver = $file.VersionInfo.FileVersion
        Write-Host ("{0}  {1}  version {2}" -f $name, $hash, $ver)
    }

    if ($missing.Count -gt 0) {
        Write-Error ("Missing from the {0}: {1}. Try -FromSource." -f ($(if ($FromSource) { 'build output' } else { '.msi' })), ($missing -join ', '))
        exit 1
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
