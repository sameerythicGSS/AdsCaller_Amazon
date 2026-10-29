# Ads Caller: installation and migration

Ads Caller supports Android, Amazon, and CrossPromo through separate runtime assemblies. Android and Amazon are alternative Online providers; install only one. Core API 3 public type names, including the historical `Out*` interfaces and enum names, remain intact for compatibility. Ads Caller is the product name in filenames, assemblies, menus, windows, and documentation. Package identities use the `com.adscaller.family.*` reverse-domain family. UPM author: Sameer Khan.

## Choosing providers after import

When Online ads and Cross Promo are both installed, Ads Caller opens a provider selection modal after compilation. The same window is available through **Ads Caller > Manage Providers**.

- **Keep both / Hybrid** retains both providers and preserves current scene configuration. New scenes use the Setup Wizard's existing hybrid defaults.
- **Online ads only** previews removal of Cross Promo assets and reconciles affected scenes, prefabs and owned Addressables entries.
- **Cross Promo only** previews removal of Online assets, exclusively used MAX/AdMob dependencies and their Android Gradle declarations. SDKs referenced by other scripts, assemblies or packages stay installed.

Removal creates `AdsCallerProviderBackups/<timestamp>/` before making changes. Game code that references a removed provider must be migrated first; the review lists those references and blocks deletion. Save affected scenes before removal. Shared project packages such as Addressables, Newtonsoft and uGUI are retained. The window cannot remove the last installed provider. **Decide later** changes no provider assets.

To restore a removal, close Unity and copy the backup's Assets, Packages and ProjectSettings files over the project, then reopen Unity. Provider SDK logic, initialization and ad playback are unchanged by this Editor feature.

## Drag-and-drop installation through UPM

Export through **Ads Caller > Package Exporter** as before. Package names, output folders, Main/CrossPromo/Combined choices, demo inclusion and the network-SDK toggle retain their existing controls. The resulting `.unitypackage` is an installer envelope: drag it into the destination Unity editor and click **Import** with all installer files selected. No Python, terminal or external application is required.

An independent editor bootstrap unpacks the selected modules directly into `Packages/com.adscaller.family.*`. Each module appears in Package Manager with its own name, version, dependencies and author **Sameer Khan**. Only the small reusable installer remains at `Assets/AdsCallerInstaller/Editor`; successful installation removes its payload. Keep the embedded package folders in source control.

CrossPromo declares Addressables and Newtonsoft JSON in `package.json`. The installer asks Unity Package Manager to resolve missing public dependencies before provider scripts are imported. Internet access or an existing package cache is required. Existing direct package pins are preserved; incompatible existing versions must be resolved in Package Manager. Input System remains optional.

| Package | Role |
| --- | --- |
| `com.adscaller.family.core` | Shared Core, prefabs and independent editor tools |
| `com.adscaller.family.main.android` | Android Main provider |
| `com.adscaller.family.main.amazon` | Amazon Main provider |
| `com.adscaller.family.cross-promo` | CrossPromo provider and Addressables integration |
| `com.adscaller.family.sdk.max` | Bundled MAX SDK |
| `com.adscaller.family.sdk.admob` | Bundled Google Mobile Ads/UMP and Android native plugin |
| `com.adscaller.family.sdk.edm` | Bundled External Dependency Manager |

Core and Main are version 5.1.0. CrossPromo 5.1.0 targets Unity 2022.3 with Addressables 1.29.0; CrossPromo 5.1.1 targets Unity 6 with Addressables 3.1.0. Both require Newtonsoft JSON 3.2.2. Use the export from the matching CrossPromo source project. Android and Amazon are alternative Main providers; remove one before switching. When SDK bundling is disabled, supply compatible SDKs separately, as with the previous exporter.

Future `main.ios`, addon and proprietary SDK packages can follow the same family. These packages are bundled locally; they are not published to Unity's public registry.

The optional maintainer tool `Tools/adscaller_upm.py` still builds local folders and tarballs for automation.

## Exporting Package Manager tarballs

In **Ads Caller > Package Exporter**, set **Export format** to **UPM tarballs (.tgz)**, then export Main, CrossPromo, Combined, or **Export All Ready**. The selection is saved per project; existing preferences default to the Unity installer format.

Each composition creates a `<package-name>-v<version>-UPM` folder containing a native `.tgz` for each selected module and a README with the dependency installation order. Module identities, versions, source values and metadata match the installer export. The version field labels the export folder; it does not override module versions. The network-SDK toggle controls SDK inclusion in both formats. Replacing a tarball export keeps the previous folder beside it as a `.previous-*` backup.

Use the destination's built-in **Package Manager > + > Install package from tarball** (or **Add package from tarball**) and install the files in the README's order. Private dependencies must be installed before their dependent modules because this family is not on the public registry. Public dependencies such as Addressables and Newtonsoft resolve through UPM. Keep referenced tarballs at a stable path and in source control. This installation path works with unrelated compilation errors, including Safe Mode, without executing the Ads Caller C# installer. Setup Wizard and scene editing still require successful compilation.

When selected demos, SDK settings or Addressables settings are available, the folder also contains `ProjectAssets.unitypackage`. After compilation, review its import list before importing those optional writable assets, preserving existing destination settings. Then run **Ads Caller > Setup Wizard**, review application identifiers, and rebuild Addressables content. These tarballs are intended for a fresh installation or a compatible UPM update. Use the migration installer for projects with legacy Ads Caller assets; tarball installation does not archive duplicates or migrate configuration. Install only one Main provider.

Installer exports preserve the source project's existing configuration values, identifiers, provider choices and optional analytics flags. Project configuration remains editable under Assets. The older maintainer tarball builder retains its separate sanitized-template policy.

## Scene setup and writable configuration

After compilation, open **Ads Caller > Setup Wizard**, choose a saved initialization scene, select installed providers, and click **Set up / reconcile scene**. Ensure your game actually loads that scene; the wizard does not reorder build scenes or change your loading flow.

The wizard creates missing components, checks for duplicates throughout the scene hierarchy, and preserves existing component settings. New Combined setup routes banners to CrossPromo and fullscreen formats to Online with fallback enabled. Existing routing is not overwritten when adding another provider; review the mediator if you intentionally change composition.

UPM configuration assets are templates. The wizard creates editable copies under `Assets/AdsCaller/Configuration` and records overrides on scene instances. An existing unique project configuration is reused. If several configurations exist, assign the intended one on the component before rerunning setup. Catalog assets referenced by package templates are also copied recursively into the project. Installer templates preserve the exporting project's current identifiers, test-ad policy and optional analytics flags. Review application-specific identifiers in the project configuration when targeting a different application.

CrossPromo setup creates project-owned Addressables settings if absent, ensures the `Salon_AdTypes` group and its seven required addresses exist, and preserves existing group settings. Conflicting addresses are reported rather than reassigned. Replace the sample catalog/database and URLs, and rebuild Addressables content for the destination application before release.

Setup completion is recorded in `ProjectSettings/AdsCallerSetup.json` only after validation and saving. Rerunning setup verifies real components rather than trusting that receipt. Existing `OutAdsCaller_SetupDone_*` dismiss preferences migrate to Ads Caller preferences (including preferences from the earlier GSS-prefixed build). The receipt contains no SDK credentials.

## Compilation errors and bootstrap prefabs

Core, Online providers, CrossPromo, and the editor bootstrap compile into separate assemblies. None depends on `Assembly-CSharp`. Ads Caller Core and public CrossPromo dependencies are handled by UPM. Vendor SDKs are supplied separately or through optional bundled snapshots.

This does not bypass unrelated C# errors or guarantee a newly imported editor window loads before Unity reloads assemblies. Unity Safe Mode prevents project/package managed code from running and does not support scene or prefab editing. Fix unrelated errors before expecting the wizard or game to run.

Main and CrossPromo packages include pre-wired bootstrap prefabs under their `Bootstrap` folders. Place a bootstrap only when normal scene editing is available, then run reconciliation after compilation. Do not add Main and CrossPromo bootstrap roots together because both contain Core components. Use the wizard to add the second provider to an existing scene.

The Android and Amazon UPM packages also expose an optional **Combined bootstrap (requires CrossPromo)** sample in Package Manager. Install CrossPromo before importing that sample, then place its single root in your initialization scene. The sample is stored in `Samples~/Combined` so it does not create missing CrossPromo references in Online-only installations. You can also generate a new composition prefab through **Ads Caller > Create Bootstrap Prefab** after compilation; existing generated assets are preserved by choosing a new filename.

## Upgrades and legacy migration

Import a newly exported installer `.unitypackage` to upgrade. Before replacing packages or archiving matching legacy files, the installer creates `AdsCallerMigrationBackups/<timestamp>/` containing the old manifest, package lock, replaced embedded packages, archived Assets and a receipt. Files are matched by GUID, including renamed legacy files. Configuration assets already in Assets keep their contents and original GUIDs; package templates have separate stable identities. Legacy UPM identifiers under `com.gss.adscaller.*` are recognized and migrated to the new family. Other installed providers remain present when importing only one module.

Scene and prefab script GUIDs are preserved. Existing setup, routing, scene loading, credentials entry and provider-choice workflows continue through the same Ads Caller menus. Demos and Addressables settings are copied into Assets only when absent; existing project files are preserved. Rebuild Addressables content for the destination application before release.

Installation errors restore replaced packages, archived files and the previous manifest. A failed payload remains available for **Ads Caller > Retry Package Installation**. For manual recovery, close Unity and restore the recorded files from the backup; consult its receipt before removing newly installed package folders.

Never import an older raw-Assets Ads Caller unitypackage over an installed UPM copy: that can introduce duplicate types and GUIDs. Use a newly exported installer instead.

## Source projects, optional adapters, and verification

The four source projects retain editable assets under `Assets`. Provider scripts moved into `Scripts/Providers/Android` or `Scripts/Providers/Amazon`, preserving `.meta` GUIDs. CrossPromo configuration scripts live under `SalonAds/Scripts/Configuration`. Shared runtime code lives in `Scripts/AdsCaller.Core.asmdef`, while shared editor code lives in `Scripts/Editor/AdsCaller.Editor.asmdef`. `AdsCallerAssetIndex.json` resolves both old paths and current package paths through GUIDs.

Firebase and AppsFlyer are optional adapters in separate assemblies, activated with the existing `ADSCALLER_FIREBASE` / `ADSCALLER_APPSFLYER` symbols when their SDKs are installed. The Android provider itself contains no direct dependency on those SDKs. Existing app-specific FirebaseManager, AdsManager, and appsflyerController code is retained under `Assets/AdsCaller/Legacy` in the Android source and excluded from new exports.

Run the release validation tools from the Android source workspace. They check all four source compiles, UPM dependency resolution in clean projects, component setup and duplicate rejection, configuration preservation, and recovery after an intentionally broken game script. Generated validation scenes live only in disposable validation projects. Native ad delivery, consent behavior, and device builds still require the existing device validation workflow.

Documentation references: [Unity package manifests](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-manifestPkg.html), [local package dependencies](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-localpath.html), and [Unity Safe Mode](https://docs.unity3d.com/6000.0/Documentation/Manual/SafeMode.html).

## Package exporter availability

Under **Export Options**, **Include catalog data** appears only when the CrossPromo plugin is detected. It defaults to enabled and is remembered per project. Disable it to export empty local game catalogs and blank remote catalog URLs in CrossPromo/Combined packages, including demo scene and prefab overrides. Both Unity installers and UPM tarballs honor this setting. The exporter transforms only packaged bytes; source assets and their GUIDs stay unchanged. Empty catalog assets remain referenced so the loader has a valid local fallback. Game textures (including PSD sources), audio and video under the CrossPromo Database are omitted together with their metadata and asset-index entries. Preview references to omitted artwork are cleared in exported prefabs and scenes. Required UI graphics, render textures, prefabs and Addressables configuration remain included. Catalog-free export requires text-serialized Unity assets.

Open **Ads Caller > Package Exporter**. Its compact window shows the current project, export folder, version, and a card for Main, CrossPromo, and Combined. Main is available when its Online provider is installed; CrossPromo is available when its own provider is installed; Combined requires both. **Export All Ready** writes every currently ready composition and skips unavailable ones. Optional demo folders are included when available and never block an otherwise complete export. The network-SDK checkbox applies only to Main/Combined and defaults off for new preferences. Existing saved choices are preserved. Leave it off when using official SDK imports.

The export folder, version, options, and three independently editable package names are stored per project in `UserSettings/AdsCallerPackageExporter.json`. Paths inside the project are stored relative to the project root; the default is `<Project>/PackageExports`. These machine-local settings do not alter package contents or internal package identifiers. Reset buttons restore the project folder or recommended composition name. Invalid filename characters are replaced and `.unitypackage` is appended automatically.

The window lists the exact blockers for each export. Missing required assets, changed canonical GUIDs, incompatible Core APIs, and invalid release manifests remain blocking errors. Local content changes (including edited prefab values) are warnings, not permanent button locks. Interactive export asks you to review those changes before exporting their current contents. It never resets the prefab or silently rewrites the release baseline.

Package cards show only a compact Ready, Warning, Unavailable, or Checking status. Less-frequent toggles live under **Export Options**, while exact blockers and local modifications live under **Preflight Details**. Preflight refreshes when project assets or options change and when the window regains focus; **Refresh** also forces a rescan. The window stays quiet in the Console during redraw, and export reruns validation immediately before writing. Exit Play Mode and wait for compilation/import to finish before exporting.

## Rewarded availability feedback

RemoveAds suppresses automatic and non-rewarded ads; opt-in rewarded ads remain eligible. In the main AdsCallerConfig or CrossPromo AdsConfiguration, enable **Show Ad Not Available Popup** to display the shared AdNotAvailable prefab for two real-time seconds when a rewarded request cannot be shown. The toggle defaults off, and the prefab reference can be replaced with a customized prefab containing AdNotAvailablePopup. A successful provider fallback and closing a rewarded ad without earning a reward do not display the popup. The popup does not block UI input and closes while the game is paused.

CrossPromo setup requires Addressables and Newtonsoft JSON. Input System is optional; fullscreen keyboard/gamepad shortcuts work with either enabled input backend.

### Analytics type compatibility

The optional Ads Caller analytics bridge is `GSS.AdsCaller.FbAnalytics`. Game-owned global `FbAnalytics` classes can coexist with the package. Existing Ads Caller prefab script GUIDs are preserved. Update custom code that intended to use the plugin bridge to its qualified name; game analytics code keeps its own type.

## Official SDK imports (5.1)

Online provider manifests depend on Ads Caller Core only. They never require an Ads Caller `sdk.*` package, even when the export also contains optional SDK snapshots. Import compatible official SDKs before installing Core and Main through Package Manager:

- **Android:** [AppLovin MAX](https://github.com/AppLovin/AppLovin-MAX-Unity-Plugin/releases) and [Google Mobile Ads with UMP](https://developers.google.com/admob/unity/quick-start).
- **Amazon:** AppLovin MAX. Google Mobile Ads remains optional under the existing `USE_ADMOB` symbol.
- **Android Firebase analytics (optional):** [FirebaseAnalytics.unitypackage](https://firebase.google.com/docs/unity/setup), including its App and Task Extension dependencies. The Android editor integration detects Firebase types and enables `ADSCALLER_FIREBASE` after compilation. No Ads Caller Firebase tarball is required. Enable Firebase in AdsCallerConfig to send analytics.

Use the vendor's supported Unity package or UPM distribution and retain its External Dependency Manager. Package identity, import directory and SDK version need not match the exporting project; the public SDK APIs must remain compatible. Ads Caller uses MAX's `MaxSdk.Scripts` assembly and Google/Firebase managed plugins directly. Breaking API changes may require an adapter update.

With official SDKs installed, install only `core`, `main.android` or `main.amazon`, and optional `cross-promo` tarballs. Skip `sdk.max`, `sdk.admob` and `sdk.edm`. These are optional snapshots for projects without an SDK installation; they must not coexist with another copy of the same SDK. Never install both Online providers. Removing an old private SDK tarball during migration must preserve project-owned SDK configuration and official imports.

EDM has no Ads Caller version or package-identity requirement. Keep any official EDM version compatible with your Unity editor and vendor SDKs, whether imported under Assets or installed through UPM. Optional MAX and AdMob snapshots also accept that installation and do not depend on `com.adscaller.family.sdk.edm`. Install the EDM snapshot only when no EDM is installed. SDK recognition uses public types and assemblies rather than matching the exporting project's package names, paths or versions; SDK API compatibility is still required.

Re-export old distributions to get the 5.1 manifests. Existing 5.0 archives remain unchanged and retain their previous private dependencies.
