# AdsCaller shared package composition — Core API 3

> Ads Caller UPM release: use [Ads Caller installation and migration](AdsCaller-UPM-Guide.md) for the current assembly layout, dependency versions, package installation, writable configuration, and compilation-error limitations. New unitypackage exports install embedded UPM packages automatically; older raw-Assets archives remain legacy distributions.


Android and Amazon each provide an Online provider; CrossPromo is optional. Choose one Online variant per project.

Shared Core, Logger, setup/export tools, branding, report writer and Build Automator must have identical contents and metadata across all release sources. CoreRelease.json records the SHA-256 of shared assets. Export preflight verifies contents, canonical GUIDs and API compatibility.

Main = Core + installed Online module. CrossPromo = Core + CrossPromo + Addressables assets. Combined = Core + both providers.

The Android module is described by AndroidNetworkModule.json. Its SDK bundle includes MAX, AdMob, UMP, their Android native plugin and EDM. Firebase and AppsFlyer are optional subscribers and are installed separately. No app-specific Gradle templates, AndroidManifest, credentials files, signing settings, machine preferences or local reports are exported.

Network configuration is Assets/AdsCaller/Data/AdsCallerConfig.asset; its script lives in Scripts/Core. Core contains no network SDK references. Network adapters live in Scripts/Network. Android and Amazon have different network configuration schemas; they are alternative variants, not additive modules.

Use matching-release packages and the CrossPromo source for the target Unity version. New CrossPromo installer exports automatically resolve Addressables and Newtonsoft JSON through UPM. Older raw-Assets exports require those packages installed first. Rebuild Addressables content for the destination application after import. UPM resolves mandatory dependencies before compilation. The independent Ads Caller setup wizard preserves existing scene components and prepares writable project configuration.

Before exporting: validate Core, export with Ads Caller > Package Exporter, then import Main followed by CrossPromo and the reverse order in disposable clean projects. Reimport both to verify upgrades. Check script compilation, serialized references, missing scripts, runtime exclusion and reward outcomes. CrossPromo-only must compile without MAX/AdMob/Firebase/AppsFlyer.

Build Automator is the complete Assets/Editor/BuildAutomator distribution with the OutBuildAutomator shim. See its README for build, deployment and CI commands. Project Reporter writes only .samtracker/report.json; it records configuration flags, installed providers and build outcomes without SDK credentials.

Android migration preserves the network config/script GUIDs while moving their paths. Shared Core GUID references and legacy mediator enum values are migrated. Remove the old placeholder AdsCallerSalon when upgrading older Android exports; never include that placeholder in a new package. Existing old unitypackage files are not changed: export fresh Core API 3 packages from these synchronized source projects.
