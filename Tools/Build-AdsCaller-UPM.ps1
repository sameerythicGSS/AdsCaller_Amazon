param([string]$Output)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$builder = Join-Path $workspace 'AdsCaller-Android/Tools/adscaller_upm.py'
if (!(Test-Path -LiteralPath $builder)) { throw 'Keep the four release source projects together to build the full release set.' }
if ($Output) { python $builder build --output $Output } else { python $builder build }
if ($LASTEXITCODE -ne 0) { throw 'Ads Caller UPM export failed.' }
