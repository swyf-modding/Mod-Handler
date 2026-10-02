<#
    Builds ScamWYF.ModHandler and installs it into the game's BepInEx\plugins.

    The shared library's build script does the work; this file only says what this mod is and
    which extra references it needs. Keeping it that way means the compiler flags and the
    reference list live in one place, in the library.

        .\build.ps1
        .\build.ps1 -CscDll C:\path\to\roslyn\csc.dll
        .\build.ps1 -GameDir "C:\...\steamapps\common\Scam With Your Friends"
        .\build.ps1 -Test

    -Test runs the library's tests as well: the behaviour tests and the API surface check. It is the
    flag a continuous build wants, since a mod that compiles against a library surface it does not
    actually use can still fail to run.
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$CscDll,
    [switch]$NoCopy,
    [switch]$Test
)

$ErrorActionPreference = 'Stop'

$lib = Join-Path $PSScriptRoot 'vendor\ScamWYF.Modding.Core\build.ps1'
if (-not (Test-Path $lib)) {
    throw "The shared library is missing. Run: git submodule update --init --recursive"
}

# The library finds the game install itself; find it here too so the extra references below can be
# pointed at the same place.
$core = $null
if ($GameDir) {
    $core = Join-Path $GameDir 'BepInEx\core'
} else {
    foreach ($root in @(
        "${env:ProgramFiles(x86)}\Steam\steamapps\common",
        "${env:ProgramFiles}\Steam\steamapps\common",
        "${env:HOME}\.local\share\Steam\steamapps\common",
        "${env:HOME}\.steam\steam\steamapps\common")) {
        foreach ($name in @('Scam With Your Friends', 'Scam With Your Friends Playtest')) {
            $candidate = Join-Path $root $name
            if (Test-Path (Join-Path $candidate 'BepInEx\core\BepInEx.dll')) {
                $GameDir = $candidate
                $core = Join-Path $candidate 'BepInEx\core'
                break
            }
        }
        if ($core) { break }
    }
}

if (-not $core) {
    throw "Could not find the game install. Pass -GameDir, or set SWYG_GAME_DIR."
}

$buildArgs = @{
    Project = 'ScamWYF.ModHandler'
    Sources = @((Join-Path $PSScriptRoot 'src'))
    OutDir  = (Join-Path $PSScriptRoot 'bin')
    Refs    = @(
        (Join-Path $core 'Mono.Cecil.dll')
    )
}

if ($GameDir) { $buildArgs.GameDir = $GameDir }
if ($CscDll)  { $buildArgs.CscDll = $CscDll }
if ($NoCopy)  { $buildArgs.NoCopy = $true }
if ($Test)    { $buildArgs.Test = $true }

& $lib @buildArgs