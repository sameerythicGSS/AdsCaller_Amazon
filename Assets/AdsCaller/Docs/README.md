# AdsCaller v1.8

> Ads Caller UPM release: use [Ads Caller installation and migration](../AdsCaller-UPM-Guide.md) for the current assembly layout, dependency versions, package installation, writable configuration, and compilation-error limitations. Any older unitypackage instructions below apply only to legacy distribution.


AdsCaller is the provider-neutral entry point for online ads and the optional CrossPromo package. Version 1.8 keeps the original game-facing API while adding hybrid routing, readiness-aware fallback, fullscreen coordination, pacing, diagnostics, and clear failure reporting.

## Start here

- [Integration Guide](Integration-Guide.md) — import, scene setup, routing, and public API.
- [Developer Guide](Developer-Guide.md) — architecture, extension contracts, analytics, and testing.
- [Changelog](CHANGELOG.md) — v1.8 changes and compatibility notes.
- [Package Composition](../Package-Composition.md) — Core/Network/CrossPromo ownership and export rules.

## Stable public API

Existing calls remain valid:

```csharp
AdsMediator.Instance.ShowInterstitial();
AdsMediator.Instance.ShowBanner();
AdsMediator.Instance.HideBanner();
AdsMediator.Instance.ShowLargeBanner();
AdsMediator.Instance.HideLargeBanner();

AdsMediator.Instance.ShowRewardedAd(
    success: () => GrantReward(),
    failure: () => ShowAdUnavailableMessage());
```

`ShowInterstitial()` does not require a callback. AdsMediator internally consumes provider outcomes for locking, fallback, pacing, analytics, and logging.

## Default v1.8 combined routing

A newly configured combined scene uses:

| Format | Primary provider | Fallback |
| --- | --- | --- |
| Banner | CrossPromo | Network |
| Large banner/MREC | CrossPromo | Network |
| Interstitial | Network | CrossPromo |
| Rewarded | Network | CrossPromo |

The setup wizard writes these settings when both provider prefabs are installed. Existing scenes preserve their serialized routing until changed in the AdsMediator Inspector.

## Diagnosing an ad that did not appear

Network load and display failures are always logged, even when `AdsCallerConfig.isTestLog` is disabled. Filter Logcat or the Unity Console using:

- `[AdsCaller/Network]`
- `[AdsCaller/CrossPromo]`
- `[AdsMediator]`
- `[AdsDiagnostics]`

Select the AdsMediator component and use **Log Ads Diagnostics** from its context menu for a provider/readiness snapshot.


## Build Automator 2.0

Open **Build Automator > Dashboard** for build presets, readiness checks, guided USB/Android
wireless/Fire TV setup, device nicknames, multiple-device deployment, Install Last Build,
build history, and app-log/storage diagnostics. Failed builds preserve previous artifacts
and restore version settings. ADB commands support cancellation/timeouts and verified results.
No game scripts or reporting package are required by the standalone Editor tool.

See the [Build Automator guide](../../Editor/BuildAutomator/README.md) for installation, troubleshooting, preset sharing,
CI environment variables, and verification. Distribute the complete tool folder and its
canonical metadata; the AdsCaller package exporter includes it automatically.

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
It is not included in package exports. See the [Build Automator guide](../../Editor/BuildAutomator/README.md) for migration,
output precedence, connection setup, storage, and troubleshooting.