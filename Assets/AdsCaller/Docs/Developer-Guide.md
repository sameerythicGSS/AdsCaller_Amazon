# AdsCaller v1.8 Developer Guide

> Ads Caller UPM release: use [Ads Caller installation and migration](../AdsCaller-UPM-Guide.md) for the current assembly layout, dependency versions, package installation, writable configuration, and compilation-error limitations. Any older unitypackage instructions below apply only to legacy distribution.


## Architecture

AdsCaller is split into three ownership groups:

- **Core:** AdsMediator, provider registry, contracts, analytics bus, logger, setup, and exporter.
- **Network:** AdsCaller, AdsCallerConfig, MAX/optional AdMob integration, and network editor tools.
- **CrossPromo:** AdsCallerSalon, catalog/cache pipeline, Addressable UI, campaigns, and presentation scripts.

Core never references a concrete optional provider. Providers register themselves using `AdsProviderRegistry`, allowing Main-only, CrossPromo-only, and combined projects to compile from the same Core revision.

## Compatibility contract

`IOutAdsProvider` remains the minimum legacy interface. Version 1.8 adds optional capabilities:

- `IAdsAvailabilityProvider` reports initialization/readiness and a human-readable reason.
- `IResultOutAdsProvider` reports a structured fullscreen outcome.
- `IPositionedOutAdsProvider` supports per-request banner positioning.

Do not add members to `IOutAdsProvider` for an optional feature. Add a focused capability interface so older and third-party providers remain compatible.

## Fullscreen lifecycle

AdsMediator performs the following sequence:

1. Check removed-ads and pacing policy.
2. Resolve the primary provider for the requested format.
3. Inspect provider availability.
4. Try the fallback provider when allowed.
5. Acquire `AdsFullscreenCoordinator`.
6. Request presentation through the result capability.
7. Release the lock on close, completion, or failure.
8. Update pacing and emit lifecycle analytics.

A provider result callback must be invoked exactly once. It should return `NotReady` or `Failed` before presentation when fallback is safe. Once UI opens, report `Closed` or `Completed`; do not ask the mediator to switch providers.

## Availability rules

Availability reasons should distinguish:

- Provider not installed/registered.
- SDK or catalog initializing.
- Format disabled by configuration.
- Creative loading.
- No fill/no eligible creative.
- Another fullscreen ad busy.
- Ads removed or blocked by consent.

Readiness is a snapshot, not a reservation. The fullscreen lock covers the race between readiness checking and presentation.

## Logging rules

A request that does not produce visible UI must generate a warning containing:

- Provider and format.
- Lifecycle phase: initialization, load, readiness, or display.
- Actionable reason.
- SDK error details when available.
- Retry timing, when applicable.

Network failures use `[AdsCaller/Network]`; routing/fallback uses `[AdsMediator]`. No-fill and provider-absent warnings must not depend on verbose/test logging.

## Analytics

`AdsAnalytics` exposes:

- Legacy named events.
- Revenue events through `AdRevenueInfo`.
- Structured lifecycle events through `AdsLifecycleInfo`.

Lifecycle stages include request, loading, loaded, load failure, show start/failure, close, reward, and fallback. Analytics integrations remain optional subscribers.

## Adding another provider

1. Implement `IOutAdsProvider`.
2. Implement readiness and result capabilities where possible.
3. Register/unregister the instance during enable/disable.
4. Avoid references to Network or CrossPromo concrete types.
5. Return a result once for every fullscreen request.
6. Emit provider-specific diagnostics.
7. Add provider assets to exporter ownership without duplicating Core.
8. Test provider-only and combined compositions.

Adding a third provider requires extending `AdsProviderKind` and the routing UI; the current automatic fallback selects the other one of Network/CrossPromo.

## Test matrix

Before release, validate:

| Composition/scenario | Expected |
| --- | --- |
| Core + Network | Compiles and serves Network |
| Core + CrossPromo | Compiles and serves CrossPromo |
| Core + both | Per-format routing and fallback work |
| Provider absent | Clear warning; no null exception |
| MAX no fill | Detailed persistent warning and retry |
| CrossPromo catalog unavailable | Network fallback where configured |
| Rapid fullscreen calls | Second request rejected as busy |
| Ads removed | No provider presentation |
| Network gate blocked | Network skipped with reason |
| Rewarded close without reward | Failure once; no duplicate reward |

## Release discipline

- Export both packages from matching Core source.
- Keep shared Core contents and `.meta` GUIDs identical.
- Increment `AdsCoreApi.Version` only for a contract-level compatibility change.
- Update both changelogs and integration guides.
- Compile runtime and editor assemblies in both source projects.
- Perform Android Logcat smoke testing because SDK callbacks cannot be fully validated by a desktop compile.


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