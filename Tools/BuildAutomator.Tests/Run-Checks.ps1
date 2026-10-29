param(
    [string]$UnityHubEditors = 'C:\Program Files\Unity\Hub\Editor',
    [string[]]$Versions = @('2022.3.62f3', '6000.0.79f1')
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = Join-Path $projectRoot '.buildautomator-verification'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$source = Join-Path $projectRoot 'Assets\Editor\BuildAutomator'
$compilerData = Join-Path $UnityHubEditors '6000.0.79f1\Editor\Data'
$compiler = Join-Path $compilerData 'DotNetSdkRoslyn\csc.dll'
$runtime = Join-Path $compilerData 'NetCoreRuntime\dotnet.exe'
foreach ($version in $Versions) {
    $data = Join-Path $UnityHubEditors "$version\Editor\Data"
    $references = Get-ChildItem -LiteralPath "$data\Managed\UnityEngine" -Filter '*.dll' | ForEach-Object { '/r:' + $_.FullName }
    $framework = Get-ChildItem -LiteralPath "$data\NetStandard\ref\2.1.0" -Filter '*.dll' | ForEach-Object { '/r:' + $_.FullName }
    $defines = '/define:UNITY_EDITOR'
    if ($version.StartsWith('6000')) { $defines += ',UNITY_6000_0_OR_NEWER' }
    $sources = Get-ChildItem -LiteralPath $source -Filter '*.cs' | Select-Object -ExpandProperty FullName
    & $runtime $compiler /nologo /target:library /nostdlib+ $defines "/out:$output\BuildAutomator.$version.dll" $references $framework $sources "$projectRoot\Assets\Editor\OutBuildAutomator.cs" "$projectRoot\Assets\AdsCaller\Scripts\Editor\AdsCallerPackageExporter.cs"
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed for Unity $version" }
    Write-Output "PASS: standalone compilation against Unity $version"
}
$frameworkData = Join-Path $compilerData 'MonoBleedingEdge\lib\mono\4.8-api'
$testReferences = @('mscorlib.dll', 'System.dll', 'System.Core.dll') | ForEach-Object { '/r:' + (Join-Path $frameworkData $_) }
& $runtime $compiler /nologo /target:exe /nostdlib+ "/out:$output\BuildAutomator.Tests.exe" $testReferences "$source\Adb.cs" "$source\OperationScheduler.cs" "$PSScriptRoot\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& "$output\BuildAutomator.Tests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Behavior tests failed' }
