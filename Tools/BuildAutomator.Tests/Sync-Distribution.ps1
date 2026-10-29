param([switch]$SyncOnly)
$ErrorActionPreference = 'Stop'
$roots = @(
    'D:\Sam_Data\Projects_Amazon\_TestProjects\TestingAmazonAds',
    'D:\Sam_Data\Projects_Amazon\_TestProjects\SalonAds-CrossPromo',
    'D:\Sam_Data\Projects_Amazon\_TestProjects\SalonAds-CrossPromo-2022'
)
$sourceRoot = $roots[0]
$toolRelative = 'Assets\Editor\BuildAutomator'
$sourceTool = Join-Path $sourceRoot $toolRelative
$utf8 = [Text.UTF8Encoding]::new($false)
function Write-Text([string]$Path, [string]$Content) { [IO.File]::WriteAllText($Path, $Content, $utf8) }

if (-not $SyncOnly) {
    $jobsPath = Join-Path $sourceTool 'BuildJobs.cs'
    $jobs = [IO.File]::ReadAllText($jobsPath)
    $old = '            if ((architectures & AndroidArchitecture.X86) != 0) list.Add("x86");'
    if (-not $jobs.Contains('#if !UNITY_6000_0_OR_NEWER')) {
        $jobs = $jobs.Replace($old, "#if !UNITY_6000_0_OR_NEWER`n$old`n#endif")
    }
    Write-Text $jobsPath $jobs
    $dashboardPath = Join-Path $sourceTool 'BuildDashboard.cs'
    $dashboard = [IO.File]::ReadAllText($dashboardPath).Replace('AssetDatabase.LoadAssetAtPath<TextAsset>(ToolFolder() + "/README.md")', 'AssetDatabase.LoadMainAssetAtPath(ToolFolder() + "/README.md")')
    Write-Text $dashboardPath $dashboard
}

# A single canonical GUID set is retained across all distributions.
$assets = @((Get-Item -LiteralPath $sourceTool)) + @(Get-ChildItem -LiteralPath $sourceTool -Recurse | Where-Object { $_.Extension -ne '.meta' })
foreach ($asset in $assets) {
    $metaPath = $asset.FullName + '.meta'
    if (-not (Test-Path -LiteralPath $metaPath)) {
        $guid = [Guid]::NewGuid().ToString('N')
        $importer = if ($asset.PSIsContainer) { 'DefaultImporter' } elseif ($asset.Extension -eq '.cs') { 'MonoImporter' } elseif ($asset.Extension -eq '.asmdef') { 'AssemblyDefinitionImporter' } else { 'TextScriptImporter' }
        $folderFlag = if ($asset.PSIsContainer) { "folderAsset: yes`n" } else { '' }
        Write-Text $metaPath "fileFormatVersion: 2`nguid: $guid`n$folderFlag${importer}:`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
    }
}

foreach ($root in $roots) {
    $backup = Join-Path $root '.buildautomator-verification\originals'
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    if ($root -ne $sourceRoot) {
        if (-not (Test-Path -LiteralPath "$backup\OutBuildAutomator.cs")) { Copy-Item -LiteralPath "$root\Assets\Editor\OutBuildAutomator.cs" -Destination "$backup\OutBuildAutomator.cs" }
        New-Item -ItemType Directory -Force -Path "$root\$toolRelative" | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $sourceTool -File) { Copy-Item -LiteralPath $file.FullName -Destination "$root\$toolRelative\$($file.Name)" -Force }
        Copy-Item -LiteralPath "$sourceTool.meta" -Destination "$root\$toolRelative.meta" -Force
        Copy-Item -LiteralPath "$sourceRoot\Assets\Editor\OutBuildAutomator.cs" -Destination "$root\Assets\Editor\OutBuildAutomator.cs" -Force
        Copy-Item -LiteralPath "$sourceRoot\Assets\Editor\OutBuildAutomator.cs.meta" -Destination "$root\Assets\Editor\OutBuildAutomator.cs.meta" -Force
        New-Item -ItemType Directory -Force -Path "$root\Tools\BuildAutomator.Tests" | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath "$sourceRoot\Tools\BuildAutomator.Tests" -File) { Copy-Item -LiteralPath $file.FullName -Destination "$root\Tools\BuildAutomator.Tests\$($file.Name)" -Force }
    }

    $exporterPath = Join-Path $root 'Assets\AdsCaller\Scripts\Editor\AdsCallerPackageExporter.cs'
    if (-not (Test-Path -LiteralPath "$backup\AdsCallerPackageExporter.cs")) { Copy-Item -LiteralPath $exporterPath -Destination "$backup\AdsCallerPackageExporter.cs" }
    $exporter = [IO.File]::ReadAllText($exporterPath)
    if (-not $exporter.Contains('        "Assets/Editor/BuildAutomator",')) {
        $exporter = $exporter.Replace('        "Assets/Editor/OutBuildAutomator.cs"', "        `"Assets/Editor/BuildAutomator`",`n        `"Assets/Editor/OutBuildAutomator.cs`"")
    }
    foreach ($asset in $assets) {
        $relative = $asset.FullName.Substring($sourceRoot.Length + 1).Replace('\', '/')
        $guid = [regex]::Match([IO.File]::ReadAllText($asset.FullName + '.meta'), 'guid: ([a-f0-9]+)').Groups[1].Value
        $entry = "            { `"$relative`", `"$guid`" },"
        if (-not $exporter.Contains($entry)) {
            $exporter = $exporter.Replace('            { "Assets/Editor/OutBuildAutomator.cs",', "$entry`n            { `"Assets/Editor/OutBuildAutomator.cs`",")
        }
    }
    Write-Text $exporterPath $exporter

    $composition = Join-Path $root 'Assets\AdsCaller\Package-Composition.md'
    $text = [IO.File]::ReadAllText($composition)
    $text = $text.Replace('+# AdsCaller package composition', '# AdsCaller package composition')
    $text = $text.Replace('- `Editor/OutBuildAutomator.cs`', '- `Assets/Editor/OutBuildAutomator.cs` (legacy entry-point shim)')
    if (-not $text.Contains('## Build Automator 2.0')) {
        $text += @'

## Build Automator 2.0

All Main, CrossPromo, and Combined distributions include the complete
`Assets/Editor/BuildAutomator` folder and `Assets/Editor/OutBuildAutomator.cs` shim.
Every tool source, README, package manifest, assembly definition, and `.meta` GUID is shared
across the Unity 6 and Unity 2022 source projects. The exporter validates the canonical GUIDs.
Do not export only the legacy shim; it depends on the new standalone Editor assembly.

See the [Build Automator guide](../Editor/BuildAutomator/README.md) for setup, USB/wireless/Fire TV
connections, presets, safe builds, deployment, diagnostics, standalone/UPM installation, and CI.
The standalone export includes only the tool folder. No AdsCaller/reporting package is required.
Version 2.1 stores the chosen build folder, devices, nicknames, and ADB preferences in the OS
account's local application-data folder, shared across all projects using the updated tool.
Presets remain in ProjectSettings; device selections and history remain in UserSettings.
Neither machine/local configuration nor signing passwords are bundled into package exports.
'@
    }
    Write-Text $composition $text

    $docsRelative = if ($root -eq $sourceRoot) { 'Assets\AdsCaller\Docs' } else { 'Assets\AdsCaller\SalonAds\_Docs' }
    $docs = Join-Path $root $docsRelative
    $link = if ($root -eq $sourceRoot) { '../../Editor/BuildAutomator/README.md' } else { '../../../Editor/BuildAutomator/README.md' }
    foreach ($file in Get-ChildItem -LiteralPath $docs -Filter '*.md') {
        $text = [IO.File]::ReadAllText($file.FullName)
        if ($text.Contains('## Build Automator 2.0')) { continue }
        if (-not (Test-Path -LiteralPath "$backup\$($file.Name)")) { Copy-Item -LiteralPath $file.FullName -Destination "$backup\$($file.Name)" }
        $section = @"

## Build Automator 2.0

Open **Build Automator > Dashboard** for build presets, readiness checks, guided USB/Android
wireless/Fire TV setup, device nicknames, multiple-device deployment, Install Last Build,
build history, and app-log/storage diagnostics. Failed builds preserve previous artifacts
and restore version settings. ADB commands support cancellation/timeouts and verified results.
No game scripts or reporting package are required by the standalone Editor tool.

See the [Build Automator guide]($link) for installation, troubleshooting, preset sharing,
CI environment variables, and verification. Distribute the complete tool folder and its
canonical metadata; the AdsCaller package exporter includes it automatically.
"@
        if ($file.Name -eq 'CHANGELOG.md') {
            $firstBreak = $text.IndexOf("`n")
            $text = $text.Insert($firstBreak + 1, "`n" + $section + "`n")
        } else { $text += $section }
        Write-Text $file.FullName $text
    }
    # Keep the shared-settings upgrade discoverable in every provider's bundled documentation.
    foreach ($file in Get-ChildItem -LiteralPath $docs -Filter '*.md') {
        $text = [IO.File]::ReadAllText($file.FullName)
        if ($text.Contains('## Build Automator 2.1')) { continue }
        $section = @"

## Build Automator 2.1 — Shared build location and devices

- In **Build Automator > Dashboard > Build**, use **Build location — shared across projects > Browse...**
  or enter an absolute folder path. The selected folder is reused by every updated project
  under the same OS account. A preset can explicitly override it under Advanced.
- Discovered and manually added devices, nicknames, and connection aliases are shared.
  Existing project device lists migrate on first dashboard open. Duplicate endpoints merge;
  USB/Wi-Fi aliases merge when a usable hardware serial verifies they are the same device.
- Output and ADB preferences refresh in other idle dashboards automatically. Shared saves
  use a lock, merge the latest state, and retain a backup.
- Target selections, presets, build history, and signing remain project-specific. An IP
  address changing to a different known device clears its selection for safety.

On Windows, shared data lives at **%LOCALAPPDATA%/OutTools/BuildAutomator/settings.json**.
It is not included in package exports. See the [Build Automator guide]($link) for migration,
output precedence, connection setup, storage, and troubleshooting.
"@
        if ($file.Name -eq 'CHANGELOG.md') {
            $firstBreak = $text.IndexOf([char]10)
            $text = $text.Insert($firstBreak + 1, [Environment]::NewLine + $section + [Environment]::NewLine)
        } else { $text += [Environment]::NewLine + $section }
        Write-Text $file.FullName $text
    }
    Write-Output "Updated distribution and documentation: $root"
}

$relativeFiles = @('Assets\Editor\OutBuildAutomator.cs', 'Assets\Editor\OutBuildAutomator.cs.meta', 'Assets\Editor\BuildAutomator.meta', 'Assets\AdsCaller\Scripts\Editor\AdsCallerPackageExporter.cs') + @(Get-ChildItem -LiteralPath $sourceTool -File | ForEach-Object { "$toolRelative\$($_.Name)" })
foreach ($relative in $relativeFiles) {
    $hashes = $roots | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $_ $relative) -Algorithm SHA256).Hash }
    if (@($hashes | Select-Object -Unique).Count -ne 1) { throw "Distribution mismatch: $relative" }
}
Write-Output "PASS: $($relativeFiles.Count) shared files identical across all three projects"
