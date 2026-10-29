param(
    [string]$Version = '6000.0.79f1',
    [string]$UnityHubEditors = 'C:\Program Files\Unity\Hub\Editor',
    [string]$ScratchSuffix = ''
)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$scratch = Join-Path $sourceRoot ".buildautomator-verification\Standalone-$Version$ScratchSuffix"
if (Test-Path -LiteralPath $scratch) { throw "Smoke project already exists. Use a fresh scratch directory to avoid changing an earlier run: $scratch" }
New-Item -ItemType Directory -Force -Path "$scratch\Assets\Editor", "$scratch\ProjectSettings", "$scratch\Packages" | Out-Null
Copy-Item -LiteralPath "$sourceRoot\Assets\Editor\BuildAutomator" -Destination "$scratch\Assets\Editor\BuildAutomator" -Recurse
Copy-Item -LiteralPath "$PSScriptRoot\EditorSmoke.cs" -Destination "$scratch\Assets\Editor\BuildAutomatorEditorSmoke.cs"
Copy-Item -LiteralPath "$PSScriptRoot\ControllerSmoke.cs" -Destination "$scratch\Assets\Editor\BuildAutomatorControllerSmoke.cs"
[IO.File]::WriteAllText("$scratch\ProjectSettings\ProjectVersion.txt", "m_EditorVersion: $Version`n")
[IO.File]::WriteAllText("$scratch\Packages\manifest.json", '{"dependencies":{}}')
$exe = Join-Path $UnityHubEditors "$Version\Editor\Unity.exe"
$log = Join-Path $scratch 'smoke.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $scratch + '"'), '-buildTarget', 'Win64', '-executeMethod', 'BuildAutomatorControllerSmoke.RunAll', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -Environment @{ OUT_BUILD_SETTINGS_PATH = "$scratch\isolated-machine-settings.json" }
Write-Output "Started Unity smoke check PID $($process.Id)"
Write-Output "Log: $log"
Write-Output "Result: $scratch\smoke-result.txt"
