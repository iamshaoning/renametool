#Requires -Version 5.1
<#
    publish.ps1 - produce both distributables in one run.

      1) full : self-contained single file, no .NET runtime required  (~68 MB)
      2) min  : framework-dependent single file, needs .NET 10 Desktop Runtime (~0.4 MB)

    Output: <repo>\dist\<AssemblyName>-<Version>-full.exe
            <repo>\dist\<AssemblyName>-<Version>-min.exe

    Nothing about the layout is hard-coded: the assembly name and version are read
    from the .csproj, the project is located via $PSScriptRoot, and both publishes
    write to explicit -o directories. Editing the project (new files, new version,
    different TargetFramework / RID) therefore does not require touching this script.

    ASCII-only on purpose: PowerShell 5.1 reads BOM-less files as ANSI, which would
    corrupt non-ASCII characters in this script.
#>

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
# Glob instead of a fixed name so renaming the project file does not break the script.
$proj = (Get-ChildItem -LiteralPath $root -Filter '*.csproj' -File |
        Select-Object -First 1).FullName
if (-not $proj) { throw "No .csproj found in $root" }

# --- read assembly name / version straight from the project -----------------
# Plain regex instead of [xml]: the .csproj is UTF-8 without BOM, and Windows
# PowerShell 5.1 decodes such files as ANSI, which turns the Chinese comments into
# garbage that can break XML parsing. Both tags matched here are pure ASCII, so
# regex is immune to that problem. -Encoding UTF8 keeps the read correct anyway.
$raw = Get-Content -LiteralPath $proj -Raw -Encoding UTF8

$assembly = [regex]::Match($raw, '<AssemblyName>\s*([^<]+?)\s*</AssemblyName>').Groups[1].Value
if (-not $assembly) { $assembly = [IO.Path]::GetFileNameWithoutExtension($proj) }

$version = [regex]::Match($raw, '<Version>\s*([^<]+?)\s*</Version>').Groups[1].Value
if (-not $version) { $version = '0.0.0' }

# net10.0-windows -> 10 ; used below to pick a dotnet host that can actually build it.
$tfmMajor = 0
$tfmMatch = [regex]::Match($raw, '<TargetFramework>\s*net(\d+)\.')
if ($tfmMatch.Success) { $tfmMajor = [int]$tfmMatch.Groups[1].Value }

# --- locate a dotnet that actually has an SDK -------------------------------
# "C:\Program Files\dotnet\dotnet.exe" is often just a runtime host; picking it
# makes publish fail with "No .NET SDKs were found". So every candidate is probed
# with --list-sdks and only a host that reports at least one SDK is accepted.
# A host only sees the SDKs under its own root (no multilevel lookup since .NET 7),
# so a machine can easily have several dotnet.exe and only one of them new enough
# for the project TFM. Candidates are therefore preferred by SDK version, and the
# first host with any SDK is kept only as a last resort.
function Find-DotnetHost {
    $candidates = @()
    if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe') }
    $candidates += @(
        (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe')
    )
    $onPath = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    if ($onPath) { $candidates += $onPath }

    $fallback = $null
    foreach ($c in $candidates) {
        if (-not $c) { continue }
        if (-not (Test-Path -LiteralPath $c)) { continue }
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $sdks = & $c --list-sdks 2>&1
        $code = $LASTEXITCODE
        $ErrorActionPreference = $previous
        if ($code -ne 0 -or -not $sdks) { continue }
        if (-not $fallback) { $fallback = $c }
        foreach ($line in @($sdks)) {
            $v = ($line -split '\s+')[0]
            if ($v -match '^(\d+)\.' -and [int]$Matches[1] -ge $tfmMajor) { return $c }
        }
    }
    return $fallback
}

$dotnet = Find-DotnetHost
if (-not $dotnet) { throw 'No dotnet host with an installed SDK was found. Install the .NET 10 SDK first.' }

$rid = 'win-x64'
$dist = Join-Path $root 'dist'
$stageFull = Join-Path $dist 'stage-full'
$stageMin = Join-Path $dist 'stage-min'

Write-Host "dotnet  : $dotnet"
Write-Host "project : $assembly $version ($rid)"
Write-Host ''

# --- stop running instances so the output files are not locked --------------
Get-Process -Name $assembly -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -LiteralPath $stageFull, $stageMin -Recurse -Force -ErrorAction SilentlyContinue

# --- 1) full: self-contained single file, compressed ------------------------
$fullArgs = @(
    'publish', $proj,
    '-c', 'Release',
    '-r', $rid,
    '-p:PublishSingleFile=true',
    '-p:SelfContained=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $stageFull,
    '--nologo', '-v', 'q'
)
Write-Host '[1/2] full : self-contained single file, no runtime needed ...'
& $dotnet @fullArgs
if ($LASTEXITCODE -ne 0) { throw "full publish failed, exit code $LASTEXITCODE" }

# --- 2) min: framework-dependent single file --------------------------------
$minArgs = @(
    'publish', $proj,
    '-c', 'Release',
    '-r', $rid,
    '-p:PublishSingleFile=true',
    '-p:SelfContained=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $stageMin,
    '--nologo', '-v', 'q'
)
Write-Host '[2/2] min  : framework-dependent single file, needs .NET 10 Desktop Runtime ...'
& $dotnet @minArgs
if ($LASTEXITCODE -ne 0) { throw "min publish failed, exit code $LASTEXITCODE" }

# --- collect the two executables into dist\ ---------------------------------
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$fullExe = Join-Path $dist "$assembly-$version-full.exe"
$minExe = Join-Path $dist "$assembly-$version-min.exe"

Copy-Item -LiteralPath (Join-Path $stageFull "$assembly.exe") -Destination $fullExe -Force
Copy-Item -LiteralPath (Join-Path $stageMin "$assembly.exe") -Destination $minExe -Force
Remove-Item -LiteralPath $stageFull, $stageMin -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Get-Item -LiteralPath $fullExe, $minExe |
    Select-Object @{n = 'File'; e = { $_.Name } },
                  @{n = 'SizeMB'; e = { [math]::Round($_.Length / 1MB, 2) } },
                  LastWriteTime |
    Format-Table -AutoSize
Write-Host "Output directory: $dist"
