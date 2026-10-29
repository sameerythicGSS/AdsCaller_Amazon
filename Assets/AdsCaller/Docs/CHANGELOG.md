# AdsCaller Changelog

> Ads Caller UPM release: use [Ads Caller installation and migration](../AdsCaller-UPM-Guide.md) for the current assembly layout, dependency versions, package installation, writable configuration, and compilation-error limitations. Any older unitypackage instructions below apply only to legacy distribution.



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

## Build Automator 2.0.1 — Keystore validation fix

- Resolve keystores through Unity instead of assuming relative paths are project-relative.
- Show configured and resolved paths in Advanced; report actual missing files with their resolved path.
- Keep ambiguous relative paths non-blocking if Unity's resolver is unavailable; final signing validation remains with Unity.
- Preserve existing signing/profile settings and passwords. Verified on Unity 2022.3 and Unity 6.



## Build Automator 2.0

Open **Build Automator > Dashboard** for build presets, readiness checks, guided USB/Android
wireless/Fire TV setup, device nicknames, multiple-device deployment, Install Last Build,
build history, and app-log/storage diagnostics. Failed builds preserve previous artifacts
and restore version settings. ADB commands support cancellation/timeouts and verified results.
No game scripts or reporting package are required by the standalone Editor tool.

See the [Build Automator guide](../../Editor/BuildAutomator/README.md) for installation, troubleshooting, preset sharing,
CI environment variables, and verification. Distribute the complete tool folder and its
canonical metadata; the AdsCaller package exporter includes it automatically.

## v1.8 — Hybrid routing and reliability

### Added

- Core API version 2 marker for same-release package validation.
- Per-format Network/CrossPromo routing.
- Readiness-aware automatic fallback.
- Provider-neutral fullscreen coordination.
- Structured ad availability and show outcomes.
- Shared startup grace, cooldown, and session interstitial caps.
- Network consent/runtime gate.
- Structured lifecycle analytics.
- AdsMediator diagnostic snapshot.
- Persistent, actionable warnings when an ad does not appear.
- Explicit MAX no-fill reporting with mediation details and retry timing.

### Changed

- `AdsMediator.Instance.ShowInterstitial()` remains parameterless and backward-compatible; outcomes are consumed internally.
- Combined setup defaults CrossPromo for banners/MREC and Network for interstitial/rewarded.
- Banner hiding now addresses both registered providers.
- Package exporter validates Core API version 2 in addition to canonical Core GUIDs.
- Removed-ads persistence now saves immediately.

### Compatibility

- Main v1.8 and CrossPromo v1.8 must be imported from the same release.
- Existing scenes keep their serialized routing. Enable advanced routing manually or configure a new scene through the setup wizard.
- Existing `IOutAdsProvider` implementations continue to compile; new reliability behavior is exposed through optional interfaces.

## Earlier releases

Earlier AdsCaller releases supplied the legacy Network provider, mediator API, analytics integrations, editor setup, and build tooling. Version 1.8 is the first release documented around Core/Network/CrossPromo composition and Core API versioning.

