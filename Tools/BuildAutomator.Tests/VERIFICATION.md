# Build Automator 2.3 verification

Date: 2026-08-28. Host: Windows.

| Check | Result |
| --- | --- |
| Standalone C# compilation against Unity 2022.3.62f3 | Passed |
| Standalone C# compilation against Unity 6000.0.79f1 | Passed |
| AdsCaller package exporter compilation with the new tool | Passed on both API sets |
| Fake-ADB process and behavior checks | 37 passed |
| Clean Unity 2022.3 Editor smoke checks | 22 passed |
| Clean Unity 6 Editor smoke checks | 22 passed |
| Shared tool, shim, exporter, and metadata contents across all three projects | 20 files identical |

The Editor smoke tests used isolated projects under `.buildautomator-verification`, not
the AdsCaller project scenes. They imported the standalone assembly without AdsCaller or
ProjectReporter, opened/closed the dashboard, built a real Windows player, deliberately
failed a subsequent build, checked version/options rollback and preservation of the prior
artifact, verified history/checksums, and exported a standalone `.unitypackage`.

Expected `BUILD_AUTOMATOR_EXPECTED_TEST_FAILURE` errors in the smoke logs belong to the
intentional failed-build test. A successful run ends with `BUILD_AUTOMATOR_SMOKE_COMPLETE`
and writes `smoke-result.txt` in the isolated project.

The final Android split-APK/OBB guards were also compiled against both Editor API sets.
No real Android APK/AAB build, USB/wireless pairing, hardware install/launch, or macOS/Linux
test was performed. Those require a device/platform smoke test. UPM installation is provided
through the manifest but was not exercised in these Editor checks.

Run `Run-Checks.ps1` for compiler and fake-ADB tests. `Start-EditorSmoke.ps1` creates a fresh
isolated Editor project and launches its checks hidden; inspect the printed log/result paths.
It refuses to overwrite an existing verification project. `Sync-Distribution.ps1` is a
maintenance script for the three explicitly listed source projects; it is not needed by
end users and is not included in the standalone package.

## Keystore regression fix (2.0.1)

Eleven keystore checks passed in each installed Editor (2022.3.62f3 and 6000.0.79f1), via
`BuildAutomatorEditorSmoke.RunKeystoreChecks` in the existing isolated projects. These cover
absolute paths with spaces, missing selection/files, a dedicated-folder path fixture that
reproduces the original false rejection, native-path precedence, safe fallback when resolution
is unavailable, debug signing, and the actual Unity native resolver for an in-project key.
Dedicated-folder precedence is tested with a supplied resolved-path fixture; the tests do not
change the user's global dedicated-keystore preference or use real signing keys/passwords.
The original 37 fake-ADB checks and both compiler/API-set checks also passed after the fix.
Results: `keystore-result.txt` and `keystore-smoke.log` in each isolated verification project.

## Shared settings (2.1)

31 shared-settings checks passed in each actual Unity Editor (2022.3.62f3 and 6000.0.79f1).
Coverage includes migration/idempotence, duplicate endpoints, USB/Wi-Fi hardware aliases,
nickname preservation, new IPs/ports, conflicting hardware on reused addresses, safe target
reselection, forget/migration tombstones, output precedence and project overrides, invalid
paths, concurrent writers, lock failures, backups, and preservation of corrupted settings.

The full 22-check Editor smoke suite also passed on both versions after these changes,
including a real Windows player build into a shared output folder containing spaces,
failed-build rollback, and standalone package export. Compiler/API checks on both versions
and all 37 fake-ADB tests passed. All 20 shared distribution files are byte-identical.

Run Start-EditorSmoke.ps1 to execute RunSharedAndBuildChecks in a fresh isolated project.
Results are machine-settings-result.txt and smoke-result.txt; the existing verification
projects' latest full runs use shared-and-build-smoke.log. Test settings are redirected via
OUT_BUILD_SETTINGS_PATH and never write to the user's shared preferences or use real devices.
Hardware identity probing and Android deployment still require a real-device smoke test.

## Wireless reconnection regression fix (2026-09-03)

- Compilation passed against Unity 2022.3.62f3 and 6000.0.79f1.
- All 57 behavior/process checks passed, including empty and pairing-only discovery,
  saved IP/mDNS fallback, new advertised ports, misleading already-connected responses,
  cooldown expiry/capping/reset, cancellation, and IP-to-mDNS connection verification.
- Each actual Unity Editor passed 41 targeted checks: 7 wireless identity checks plus
  34 shared-settings regressions, including address reassignment and selection continuity.
- All 16 files in Assets/Editor/BuildAutomator are byte-identical across TestingAmazonAds,
  SalonAds-CrossPromo, and SalonAds-CrossPromo-2022. Regression sources are also synchronized.
- Release remains the default when the dashboard opens. No game or build configuration changed.

The targeted Editor entry point is BuildAutomatorEditorSmoke.RunWirelessIdentityChecks.
It must run in a disposable .buildautomator-verification project with OUT_BUILD_SETTINGS_PATH
redirected to that fixture. Final logs are wireless-final.log in Wireless-2022.3.62f3-d0acde88
and Wireless-6000.0.79f1-a5a19f9b under TestingAmazonAds/.buildautomator-verification.
No live device was available for an end-to-end reconnect/endurance check. These tests validate
the reconnect policy and identity/selection handling, not the stability of the Wi-Fi network.

## Automatic discovery of newly visible paired devices (2.3.1, 2026-09-04)

- Every advertised `_adb-tls-connect._tcp` service is now attempted, including devices that are
  not yet in shared settings. Pairing advertisements remain excluded.
- The stable mDNS service name is tried before the advertisement's temporary IP:port. A successful
  transport is accepted only after it responds and its hardware serial is read; it is then added
  to the normal selectable device list.
- Standalone compilation passed against Unity 2022.3.62f3 and 6000.0.79f1. All 58 fake-ADB and
  behavior checks passed, and all 20 distribution files matched across the three source projects.
- The installed Platform Tools 36.0.0 reported `mdns_enabled: true` with the OPENSCREEN backend.
  No wireless device was advertising during the live check, so final device-side validation still
  requires enabling Wireless debugging on an already-paired Android device and clicking Refresh.

## Cross-Editor ADB and background refresh fix (2.3.2, 2026-09-04)

- Live diagnostics found Unity 2022.3 Platform Tools 32 and Unity 6 Platform Tools 36 repeatedly
  replacing the same machine-wide ADB server. Automatic resolution now selects the newest ADB
  installation visible beside the running Unity Editor, unless a shared override is explicit.
- Quiet refresh no longer disables the dashboard or displays the foreground-operation banner. Its
  cadence is 15 seconds, and it attempts only current advertisements in the background; stale
  endpoint fallback remains available from manual Refresh.
- Independent DNS-SD probing received other LAN mDNS traffic but no ADB advertisement from the
  connected `KFTUWI` Fire HD 10. A clean Platform Tools 36 server restart also found none. The UI
  now reports this Fire OS limitation instead of continuously retrying an unknowable random port.
- Standalone compilation passed against Unity 2022.3.62f3 and 6000.0.79f1, all 58 fake-ADB checks
  passed, and fresh isolated Unity smoke projects passed 24 checks on each Editor version.

## Shared controller, wireless groups, and recovery (2.4.0, 2026-09-07)

- Standalone compilation passed against Unity 2022.3.62f3 and 6000.0.79f1.
- All 64 process/behavior checks passed, including six new scheduler checks for foreground
  priority, discovery cancellation, serialization, failure recovery, and shutdown.
- Each clean Editor passed 29 controller/recovery checks. These exercise both windows sharing
  one controller, first-use defaults and saved-choice migration, operation completion after
  window closure, paired Wi-Fi appearance/changed ports, available pairing advertisements,
  deselection and address-reuse safety, and interrupted/committed publication recovery.
- Existing wireless identity and shared-settings regressions passed in both Editors.
- Each clean Editor passed 25 build/export checks, including a real Windows player build,
  usable output paths in report/publication callbacks, long staging paths on Windows, an
  intentionally failed rebuild at the exact same output filename, version rollback, artifact
  and manifest preservation, and standalone package export.
- An initial real-build run exposed Windows managed file access failing on long staged paths;
  extended Windows filesystem paths fixed this, and both complete Editor suites passed afterward.
- Latest complete fixtures: Standalone-2022.3.62f3-Publish24-0907 and
  Standalone-6000.0.79f1-Publish24-0907 under .buildautomator-verification.
  Each has controller-result.txt, machine-settings-result.txt, wireless-result.txt,
  smoke-result.txt, and smoke.log. Completion marker: BUILD_AUTOMATOR_ALL_COMPLETE.
- Live ADB devices and mDNS services were empty on this host. Hardware Wi-Fi reconnection,
  pairing, and installation were not end-to-end verified. macOS/Linux remain unverified.
  Wi-Fi test results above use an injected ADB boundary and do not establish network endurance.

Start-EditorSmoke.ps1 now launches BuildAutomatorControllerSmoke.RunAll in a hidden disposable
Editor and lets that entry point exit only after its asynchronous checks and build suite finish.

