<#
.SYNOPSIS
  Fetches the Skua interface assemblies the Questkeeper plugin compiles against.
.DESCRIPTION
  Plan A (default): download the official x64 .msi from the Skua release and unpack it
  with "msiexec /a" (administrative install, nothing is installed).
  Plan B (-FromSource): clone the release tag and build the two library projects.
  Nothing downloaded here is committed; the destination is git-ignored.
#>
[CmdletBinding()]
param(
    [string]$Version = '1.4.5.0',
    [string]$Destination = (Join-Path $PSScriptRoot '..\..\plugin\QuestKeeper.SkuaBridge\lib\skua'),
    [switch]$FromSource
)

$ErrorActionPreference = 'Stop'

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
        Invoke-WebRequest -Uri $url -OutFile $msi
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
