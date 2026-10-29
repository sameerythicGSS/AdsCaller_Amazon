# AdsCaller v1.8 Integration Guide

> Ads Caller UPM release: use [Ads Caller installation and migration](../AdsCaller-UPM-Guide.md) for the current assembly layout, dependency versions, package installation, writable configuration, and compilation-error limitations. Any older unitypackage instructions below apply only to legacy distribution.


## 1. Package compatibility

Main AdsCaller v1.8 uses shared Core API version 2. Import Main and CrossPromo packages from the same release. Shared Core assets intentionally use identical Unity GUIDs and must not be edited as separate copies.

Supported compositions:

| Installation | Contents |
| --- | --- |
| Main only | Core + Network |
| CrossPromo only | Core + CrossPromo |
| Combined | Core + Network + CrossPromo |

Remove any legacy placeholder `AdsCallerSalon.cs` before upgrading an older Main installation.

## 2. Import and setup

1. Import the Main v1.8 package.
2. If CrossPromo is required, install Addressables and Newtonsoft JSON, then import CrossPromo v1.8.
3. Open **Ads Caller > Setup Wizard**.
4. Select an enabled boot scene.
5. Enable the provider modules to inject.
6. Run setup and enter network credentials when prompted.
7. Confirm there is exactly one AdsMediator, one instance of each selected provider, FbAnalytics, and SEHandler.

For combined installations, setup enables advanced per-format routing and fallback automatically. Re-running setup on an already configured scene can create duplicates; for an existing scene, edit the AdsMediator routing fields directly.

## 3. Network configuration

Configure the `AdsCallerConfig` asset:

- MAX SDK key and ad-unit IDs.
- Enable/disable banner, MREC, interstitial, and optional app-open formats.
- Interstitial cooldown and presentation delay.
- Banner scene-change behavior.
- Test logging and mediation debugger.

AdMob app-open support remains optional and is compiled only when `USE_ADMOB` is defined and the Google Mobile Ads SDK is installed.

## 4. Game-facing API

### Interstitial

```csharp
AdsMediator.Instance.ShowInterstitial();
```

No result callback is required. If the primary provider is absent or not ready, AdsMediator can try the configured fallback. Failures and pacing decisions are logged.

### Rewarded

```csharp
AdsMediator.Instance.ShowRewardedAd(
    success: () => playerWallet.AddCoins(100),
    failure: () => rewardButton.SetInteractable(true));
```

The reward callback is guarded so it is granted once. A provider is never switched after an ad has opened.

### Banners

```csharp
AdsMediator.Instance.ShowBanner();
AdsMediator.Instance.ShowBanner(EnumsJar_Salon.AdPosition.BottomCenter);
AdsMediator.Instance.HideBanner();

AdsMediator.Instance.ShowLargeBanner();
AdsMediator.Instance.HideLargeBanner();
AdsMediator.Instance.HideAllBanners();
```

Positioned calls use the optional positioning capability where supported and fall back to the provider default otherwise.

## 5. Hybrid routing and fallback

Enable `useAdvancedRouting` on AdsMediator to select a provider independently for banner, large banner, interstitial, and rewarded formats. `allowProviderFallback` controls whether the other installed provider may be tried before presentation starts.

Fallback may occur for:

- Provider absent or disabled.
- Provider still initializing.
- No ready/filled creative.
- Load or display failure reported before presentation.
- CrossPromo catalog or UI not ready.

Fallback does not bypass removed-ads state, consent gates, shared pacing, or an active fullscreen lock.

## 6. Pacing

AdsMediator owns shared pacing across providers:

- `startupInterstitialGraceSeconds`
- `minimumFullscreenIntervalSeconds`
- `maximumInterstitialsPerSession`

The Network provider retains its own interstitial cooldown as a final provider-specific guard.

## 7. Consent/runtime gate

Network ads are allowed by default for backward compatibility. A consent system or remote configuration can gate them before initialization:

```csharp
AdsRuntimeGate.SetNetworkAdsAllowed(
    allowed: userConsentAllowsPersonalizedOrContextualAds,
    reason: "Consent flow completed");
```

CrossPromo remains independently available and can serve as fallback when policy permits.

## 8. Diagnostics

Call:

```csharp
AdsMediator.Instance.LogDiagnostics();
```

A network no-fill message includes the MAX error code, message, mediated-network details, load information, latency, and retry delay. `NO FILL` means no eligible mediated network returned an ad for that request; it is not the same as a missing provider.

## 9. Upgrade checklist

- Import Main and CrossPromo from the same v1.8 release.
- Confirm `AdsCoreApi.Version == 2`.
- Remove legacy placeholder providers.
- Check existing AdsMediator routing because serialized scenes are not silently rewritten.
- Run both provider-only and combined smoke tests on device.
- Exercise no-fill, offline, removed-ads, consent-blocked, and rapid-repeat request cases.


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