# Build Automator 2.5.1

An Editor-only build and device dashboard for Unity 2022.3 and Unity 6. No AdsCaller,
network SDK, reporting package, account, or runtime component is required.

The Dashboard and Full Controls use a spaced, card-based layout inspired by WebGL
Studio. A bright green **Connected** badge beside a device name means at least one
of its live USB/Wi-Fi transports is authorized and ready in ADB. Saved, offline,
unauthorized and discovery-only entries do not receive the badge. The checkbox
still controls deployment selection; a connection alone does not select a device.
The transport status below the name describes the currently chosen connection.

## WebGL Studio (2.5)

Version 2.5.1 preserves Unity's default Editor styles and fonts during redraws and keeps
the header, tabs and status footer visible while the body scrolls in smaller windows.

When WebGL is the active Unity platform, **Dashboard**, **Build** and **Build and Run**
open a dedicated UI Toolkit window. **Build Automator > WebGL Studio** also opens it
directly. Its menu item is registered only while WebGL is selected and is hidden on Android
and other platforms. Android keeps its normal dashboard and Full Controls shortcuts.
An already-open Android dashboard shows the Studio entry when switching to WebGL.
The tool never switches the project's platform automatically.

- **Build:** shared presets, version and output folder; four delivery recipes; collapsed
  advanced settings; scene/module/build readiness; Build WebGL and Build & Preview.
- **Preview:** select the latest release, choose a prior history entry, or browse to any
  WebGL build containing `index.html`. Run a local HTTP server, copy the URL, see request
  counts and bytes served, or stop it. No Node/Python or additional packages are needed.
- **Ship:** analyze actual local files, compare total size with a previous distinct release,
  review a download budget and an ideal transfer estimate, inspect the largest files and
  response headers, export an upload ZIP, and save a separate hosting kit/checklist.

| Recipe | Compression | Fallback | Cache / hash names | Development |
| --- | --- | --- | --- | --- |
| Fast iteration | Disabled | Off | Off | On |
| Portable host | Gzip | On | On | Off |
| Gzip server | Gzip | Off | On | Off |
| Brotli / HTTPS | Brotli | Off | On | Off |

Existing presets inherit current Unity WebGL Player Settings until **Use this preset's
WebGL settings** is enabled or a recipe is chosen. The recipe edits the selected preset,
including its shared development flag. Overrides are applied only during a WebGL build;
compression, fallback, browser caching, hashed filenames and exception support are restored
after success/failure and through the interrupted-build journal. Template, graphics, memory,
texture formats, remote content and other platform settings remain in Unity Player Settings.
The existing output lock, staging/publication, history and command-line entry point are reused.
CI WebGL builds use the saved preset's WebGL settings without opening the window.

Preview binds only to `127.0.0.1`, serves GET/HEAD, supplies correct `.wasm`/JavaScript MIME
and `.gz`/`.br` Content-Encoding, and deliberately omits Content-Encoding for `.unityweb`
fallback files. It prevents traversal and linked-file access, disables browser HTTP caching,
and optionally supplies COOP/COEP isolation headers. It stays running when the window closes,
stops before the next WebGL build, and stops on assembly reload/Editor exit. Port 0 chooses
an available port; changes to port/isolation require restarting the server.
Isolation may block external resources that lack the required cross-origin headers.
Brotli uses HTTP in this loopback preview and is not supported uniformly there; test Brotli
on an HTTPS staging host, or use Gzip locally. IndexedDB/Unity caching is separate from HTTP
cache control; clear browser site data when testing persistent data or cached remote content.

The upload ZIP places `index.html` at its root, preserves relative asset paths, excludes hidden
tool folders, and writes atomically outside the build folder. Cancellation preserves the
previous ZIP. The separate hosting kit contains `_headers`, `nginx-webgl.conf` and `DEPLOY.txt`.
It is generated for review and installation on your own host; the tool does not upload builds
or configure servers. `_headers` only works on providers supporting that convention; review
the nginx include with `nginx -t` in your actual HTTPS server. Compression fallback or disabled
compression is suitable for hosts where custom response headers cannot be configured.
ZIP uploads to HTML game hosts still depend on their file limits, iframe and browser support.

Size/transfer estimates cover local artifacts only; they do not measure load time, runtime
memory, browser performance or remote Addressables. Preview cannot certify production headers
or mobile compatibility. Versioned filenames must remain distinct to compare previous releases;
history entries that resolve to the same replaced folder no longer contain the old artifact.

Research focused on integrating preparation, browser iteration and release handoff. Existing
tools cover adjacent needs such as [WebGL optimization](https://assetstore.unity.com/packages/tools/utilities/webgl-optimizer-230446)
and [Unity's publishing workflow](https://learn.unity.com/learn/tutorial/create-and-publish-webgl-builds);
the Studio combines these workflow steps locally without requiring an account or runtime SDK.
This is a design direction, not a claim that no competing tool has similar functionality.
Technical behavior follows Unity's [deployment guidance](https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-deploying.html)
and [server configuration samples](https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-server-configuration-code-samples.html).

## Shared controller and wireless workflow (2.4)

- **Explicit actions take priority.** Clicking Build, Refresh, Connect, or another device action
  cancels quiet discovery, waits for its current client command to stop, then executes the action.
  Background polling cannot swallow a click or jump ahead of pending foreground work.
- **Only conflicts are disabled.** During installation you can browse History, inspect or edit
  the next build's settings, and navigate either window. Build/deployment/ADB actions sharing the
  occupied operation remain disabled. A run snapshots its preset, output, version, and targets.
- **Remembered presets.** Release is the first-use default. Advanced exposes a configurable
  First-use default, stored with exported project presets. Once selected, a user's preset is
  remembered by name, including when either window reopens. Existing saved indices migrate.
- **Paired Devices** retains Wi-Fi devices after a verified TLS connection, including while offline.
  USB and manually saved transports remain visible separately. Hardware identity and service names,
  never model names alone, preserve target selection after address/port changes.
- **Available Devices** highlights current pairing advertisements and connection advertisements
  whose authorization has not been verified. Choose Pair this device to fill the pairing endpoint
  and enter the code shown on the device. Advertisements alone do not prove that this host is paired;
  the tool never automatically pairs, reads pairing secrets from the device, or scans the subnet.
- Missing signing passwords and tool setup are accessible directly from the compact workflow.
  Unavailable selected devices remain visible with authorization/reconnection guidance.

A single controller in each Editor stays active even when its windows close. Automatic discovery
uses a bounded round, service/endpoint backoff, and current advertisements. Healthy connections and
ADB's shared server are preserved. The interval is a polling cadence, not a guaranteed connection
time: sleeping devices, unavailable mDNS, network restrictions, and hardware behavior still matter.

Build recovery uses a journal in `UserSettings/OutBuildAutomator-build.json` and a temporary
`.buildautomator/<run-id>` directory under the output folder. An output lock prevents two Editors
from publishing into that folder concurrently. After interruption, the next controller/build
recovers temporary Player Settings and rolls back incomplete publication. Completed publication
remains intact. Recovery does not rebuild, install, or launch anything automatically.


## Start in one minute

1. Open **Build Automator > Dashboard** (the existing Build / Build and Run shortcuts also open it).
2. Choose your platform in Unity's Build Settings / Build Profiles. Enable the scenes to build.
3. Set the version, build code, and build folder in the compact dashboard.
4. Connect a device, click **Refresh**, and select each target device.
5. Click **Build** or **Build & Run**.

**Build** does not require an Android device or ADB. Unity's Android build toolchain is still
required for Android builds. For Fire TV, choose APK, not Android App Bundle. Disable Split Application Binary and per-architecture split APKs: automatic OBB/split-APK deployment is not supported.

The compact dashboard shows the active preset and normal build workflow. **Show Full Controls**
opens another view of the same controller, preferences, devices, and operations. Its tabs expose
history, advanced configuration, diagnostics, and standalone tool export. Both views use the same
output precedence; opening another window never changes the build destination.

## Shared build location and devices (2.1)

On the main **Build** tab, enter an absolute **Build location â€” shared across projects** or
click **Browse...**. Choose it once; all projects running Build Automator 2.1 under the same
OS account use it. It is not limited to `[Project]/Builds`. The **Effective output** line shows
the exact destination directory. Artifacts are written directly into it, without preset or
timestamp subfolders.

Output precedence: explicit CI `OUT_BUILD_OUTPUT`, then an enabled **Project-only output override**,
then the shared folder, then the preset's project folder if no shared location has been chosen.
The per-preset override is under Advanced and defaults off. Clearing the shared field restores
project fallbacks. A missing/disconnected shared drive causes a build error; the tool does not
silently redirect the build somewhere else. A build snapshots its output before starting.

Use **Add copy destination...** below the primary build location to choose any number of
additional absolute folders. Edit, browse, or remove each destination in the list. These
destinations are shared across projects and are snapshotted when you start a build.
The tool builds once at the effective output, then copies only the main artifact (for example,
the APK or AAB) to each destination using the same filename. Manifests, automator helper files,
and sibling files stay at the primary location. For a folder-based artifact such as WebGL,
the main artifact folder is copied recursively. A desktop executable copied alone may still
need its Unity companion files to run.

Copies replace matching artifacts after preparing the new copy, leaving unrelated files alone.
Duplicate destinations and the primary location are skipped; nested locations are rejected.
A failed build creates no copies. If one copy fails, the primary build remains successful and
other destinations are still attempted. Copy successes and errors appear in build history and
the primary manifest; copy destinations receive no automator manifest. Command-line builds
also use the shared copy destinations.

Saved devices, connection aliases, nicknames, the optional ADB executable override, automatic
refresh, and reveal-on-success preferences are shared too. **Save device for all projects**
remembers a manual endpoint without requiring it to be online. **Connect** also saves it.
Other open dashboards pick up changes while idle (normally within two seconds).

Project versions, presets, selected deployment targets, signing settings/passwords, and build
history remain project-specific. This avoids unexpectedly deploying another project's app to
a device chosen elsewhere. One ready device can be selected automatically on first setup;
explicitly clearing/deselecting it is respected. A verified address changing hardware identity,
or a selected device being forgotten elsewhere, requires explicit selection again.

## Automatic wireless discovery and reconnection (2.3.2)

A changing Wi-Fi address or port is treated as a temporary transport, never as the device's
identity. A saved device retains its verified hardware serial and verified mDNS service
instances. On every automatic or manual refresh, Build Automator first lets ADB reconnect
paired devices normally, then discovers every current `_adb-tls-connect._tcp` advertisement.
It connects by the stable mDNS service name first and uses that advertisement's temporary
endpoint only as a fallback. A newly discovered, previously paired device is verified, registered
by its hardware serial, and added to the device list without requiring a manual IP or port.

After connection, Build Automator reads `ro.serialno` / `ro.boot.serialno` again. The new
address is accepted and the project's existing target selection follows it only when it resolves
to the same saved hardware identity. Only ADB TLS connection advertisements are auto-connected;
pairing is never automatic, and an address reused by different hardware clears rather than
steals the old selection. Failed reconnects use an in-memory exponential backoff up to one
minute. Full Controls displays ADB's mDNS server status when supported.

All open projects resolve the newest installed Platform Tools executable, unless an explicit
shared override is set. This prevents older and newer Unity Editors from repeatedly replacing the
machine-wide ADB server. Quiet discovery runs every 3 seconds after the previous round completes (configurable from 2â€“30
seconds) and does not disable the dashboard. Only advertised connection services are attempted in
the background; selected/known paired devices take priority over unknown advertisements. Manual Refresh also retries saved
fallback transports.

The shared registry normalizes repeated host/IP endpoints and ports. When a usable Android
hardware serial is reported, USB and Wi-Fi aliases (including changed ports) share one device
card with a **Connection** selector. Devices with matching model names alone are not merged.
Devices without a reliable serial remain separate by transport. No software can safely identify
a device that reports a misleading/duplicate hardware serial; review targets before deploying.
**Forget in all projects** removes its saved metadata, not Android pairing authorization.

Existing version 2.0/2.0.1 local device lists merge automatically the first time each project's
dashboard opens. Migration does not overwrite newer shared nicknames/preferences and records
completed projects to avoid repeat imports. A forgotten device is not resurrected by stale
migration from another project. Live discovery or explicitly adding it can register it again.
No existing project output folder is silently promoted to the shared location: choose that
location deliberately using Browse.
## Install in another project

Choose ONE installation method; do not keep duplicate copies of the tool assembly:

- **Folder:** copy the complete `BuildAutomator` folder, including its `.meta` files, into `Assets/Editor`.
- **Unity package:** use **Build > Advanced > Export standalone Build Automator package**,
  then import the generated `.unitypackage` in the destination Editor.
- **UPM:** copy the folder outside `Assets`, then use Package Manager's **Add package from disk**
  and select its `package.json`. Alternatively, copy it to `Packages/com.out.build-automator`
  as an embedded package. Remove the Assets copy before importing the UPM copy.

`Assets/Editor/OutBuildAutomator.cs` is an optional legacy entry-point shim. AdsCaller includes
it to retain `BuildOnly()`, `BuildAndRun()`, and `GetAdbPath()` callers. Standalone installs do
not need it. Old low-level public build/cleanup/dialog methods were replaced by the dashboard
and the `OutBuild` API; custom integrations using those methods need to migrate.

No SDK binaries are bundled or silently downloaded. Install **Android Build Support**, its
**SDK/NDK**, and **OpenJDK** for the matching Unity Editor through Unity Hub.

## Connect devices

### USB phones and Fire tablets

Enable Developer options and USB debugging, connect a USB **data** cable, unlock the device,
and approve the computer's RSA debugging prompt. On Windows, install the manufacturer's USB
driver if needed. Fire tablets can require Amazon's driver. Then refresh the device list.

### Android wireless debugging

On supported Android 11+ phones, enable Wireless debugging on the same Wi-Fi network as the
computer. Choose **Pair device with pairing code** on the device. In the tool's Android Wi-Fi
guide, enter the exact pairing IP:port and six-digit code. TV and other device support varies.

Pairing and connection ports are different. After pairing, click **Refresh devices** or
**Discover Wi-Fi**. Build Automator automatically connects each advertised, previously paired
device and adds it to the list where it can be selected or deselected. If discovery fails, enter
the connection IP:port shown on the main Wireless debugging screen.
Ports can change; refresh the advertised services rather than guessing an old port.

With automatic refresh enabled, saved devices reconnect using their current advertised connection
port. If discovery is temporarily empty, a manual Refresh can retry the last saved IP:port and mDNS
service name, one undiscovered device per refresh. Quiet discovery waits for a current advertisement. Selected devices take priority. Retry delays apply to
each address (up to 30 seconds), so a newly advertised port can be tried immediately. Connections
are checked for responsiveness; stale wireless transports are recovered individually. Healthy
connections, the ADB server, and pairing keys are left running. A verified manual connection also
teaches the tool its advertised service identity for subsequent automatic recovery.

If both the port changes and mDNS remains unavailable, the new connection IP:port must still come
from the device's Wireless debugging screen. The tool does not scan unknown ports.

Some Fire OS 8 devices have a platform bug where Wireless debugging publishes no ADB mDNS
service. No host tool can safely infer that session's random port in this state. Use the current
IP:port once, connect over USB, or opt into legacy `adb tcpip 5555` according to your network's
security policy. Build Automator reports the missing advertisement instead of continuously
retrying stale random ports.

Pairing codes are cleared from the field on submission and are not logged or stored in settings.
ADB itself manages the host's pairing keys outside this tool.

### Fire TV / manual network connection

For Fire OS devices that support ADB, enable ADB debugging under Developer Options, obtain
the device's IP address, and connect on the same trusted network, usually using port `5555`.
Approve the debugging request on the TV. This is distinct from Android wireless pairing.

The tool lists existing ADB connections and advertised mDNS services; it does not scan a subnet,
pair without authorization, enable debugging remotely, or bypass device security. Network
isolation, firewalls, VPNs, sleeping devices, and unsupported firmware can prevent detection.
Do not expose ADB to the internet. IPv6 endpoints use `[address]:port` syntax.

### Device state and selection

- **Ready:** can install and launch.
- **Unauthorized:** approve the debugging prompt on the device.
- **Offline:** reconnect USB/Wi-Fi and check debugging settings.
- **No permissions:** check host USB permissions/driver.
- **Saved:** previously seen transport, currently unavailable. Reconnect or forget it.

USB, emulator, mDNS, and IP transports are retained in the shared registry. Verified aliases
appear on one card; select the connection you want to use. If hardware identity cannot be
verified, transports remain separate. Nicknames are shared, selected targets are per project.
Use **Clear selection** to clear unavailable targets, or reconnect them before Build & Run.

## Presets and versioning

| Preset | Development build | Suggested code increment | Android output |
| --- | --- | --- | --- |
| Local Test | Yes | No | APK |
| QA | Yes | +1 | APK |
| Release | No | +1 | APK |

Presets do not switch platforms, signing keys, CPU architectures, scripting backends, or scenes.
They supply explicit build options and output rules for Unity's active platform. Unity 6's
active profile scene override is respected. Effective Player Settings come from the active
platform/profile; this is not a separate build-profile manager. Preset development flags take
precedence over the Editor's development checkbox for this build and are restored afterward.

Review the suggested build code explicitly. Android codes cannot decrease below the current
project code. This cannot determine the newest code in a store or on every device. Successful
builds retain the reviewed version/code; failures restore the previous values. Signing
passwords are supplied through password fields and retained only in Unity's Editor session.
They are not exported or written to the tool's JSON files.

Filename tokens: `{package}`, `{version}`, `{code}`, `{preset}`, `{target}`.
Each successful build publishes `<output>/<filename>`. Unity first builds in an isolated staging
folder on the output volume. On success, the tool publishes the artifact and companion files,
backing up replaced entries until publication commits. A failed build leaves the prior artifact intact.
Paths containing spaces are supported. Outputs under Assets, Packages, ProjectSettings,
UserSettings, Library, Temp, or tool configuration directories are rejected.

Builds with different generated filenames remain side by side. Builds resolving to the same
filename replace the previous artifact. History retains the latest 100 metadata entries;
dropping an old history entry does not delete its artifact.


### Keystore validation fix (2.0.1)

The readiness check uses Unity's resolved keystore path. `PlayerSettings.Android.keystoreName`
can be a display path relative to either the project or the dedicated keystore location;
combining it with the project root can falsely report a missing keystore. The tool no longer
makes that assumption. The native resolver reads the effective signing settings without
changing the active profile, key, alias, or passwords.

Under **Build > Advanced**, **Configured keystore** and **Resolved keystore** show the values
used by preflight. A confirmed missing file still blocks a build and displays its resolved path.
If the native helper is unavailable and a relative path is ambiguous, the tool shows an
informational note and leaves final validation to Unity instead of disabling the build buttons.
After updating, let Unity recompile, then reopen the dashboard if it still shows the old warning.
See [Unity's keystoreName documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PlayerSettings.Android-keystoreName.html).

## Install, recover, and diagnose

Select multiple ready devices to build once and install sequentially. A failed device does not
prevent trying the remaining devices. Device identity is always passed explicitly to ADB.
Install/launch results are reported separately from the build result.

- **Install Last Build / History > Install & Run:** uses an existing successful APK and verifies
  its recorded SHA-256. Modified APKs must be rebuilt; arbitrary APK import is not supported.
- **APK compatibility:** checks device readiness, minimum Android API, and supported ABIs when
  the device reports them. ADB installation remains the final compatibility check.
- **Check device storage:** displays `/data` storage for one selected ready device.
- **Read app logs:** captures up to 300 log lines for the currently configured app's main PID.
  The app must be running and the device must support `pidof` / PID-filtered logcat. This is
  a snapshot, not continuous logging or a complete multi-process/crash archive.
- **Save diagnostic text:** exports the captured logs/storage output plus Unity/ADB/app details.
  Review app logs for sensitive information before sharing.
- **Uninstall current app:** requires explicit confirmation and deletes that app's data on the
  selected device. Use Install Last Build afterward. No automatic uninstall/downgrade occurs.

Signature mismatch: use the original signing key, or explicitly uninstall if data loss is acceptable.
Version downgrade: increment the code rather than silently forcing a downgrade.
Insufficient storage: free device storage, then retry installation.
Launch failure: the APK may already be installed. Both phone and TV/Leanback launchers are checked.

ADB commands run in background workers with bounded output, exit-code/semantic checks, timeouts,
and cancellation. Discovery/connect commands use short timeouts; installation allows three
minutes. Closing a window does not cancel work. The shared controller continues discovery and foreground
operations, and saves deployment progress/results independently of any window. Explicit Cancel,
assembly reload, and Editor shutdown cancel the current ADB client operation. They do not kill the
shared ADB server or undo installation work already sent to a device. An interrupted deployment is
reported on reopening and is never retried automatically. Unity builds remain synchronous and use
Unityâ€™s progress/cancel support.

## Storage and upgrades

- `%LOCALAPPDATA%/OutTools/BuildAutomator/settings.json` on Windows: shared build location,
  saved devices/aliases/nicknames, optional ADB override, and general preferences for all projects
  under this OS account. On other hosts, the OS local-application-data directory is used;
  Advanced shows the exact path. No signing passwords or pairing codes are stored here.
- `ProjectSettings/OutBuildAutomator.json`: project preset definitions; commit if useful.
- `UserSettings/OutBuildAutomator.json`: project selection/history, plus retained legacy fields
  used only for migration; exclude from source control and package exports.
- Shared writes use an interprocess lock and reload the current file before applying a change,
  so one Editor cannot overwrite unrelated settings added by another. Corrupted shared data
  is reported, not silently replaced with defaults. The `.lock` file can remain while idle.
- `<artifact>.build-manifest.json`: per-build metadata alongside the artifact. Local history stores later
  deployment results; the manifest describes the original build.
- JSON writes use a temporary file plus a backup of the previous version (`.bak`).

Legacy global `EditorPrefs` are not automatically copied: they mix different projects' output
paths and device state. They are left untouched. The new dashboard starts with safe defaults.
Existing output artifacts and Player Settings are not changed by installation.

AdsCaller package exports include the full tool folder and the legacy shim. The tool has its
own Editor-only assembly. `SamProjectReporting` remains optional and continues to receive
Unity's normal post-build callback when installed. Custom listeners can subscribe to
`OutBuild.BuildJobs.BuildCompleted` for Unityâ€™s report while the staging output still exists.
Normal Unity post-build callbacks also see the staging path. Integrations that need the final
published path should subscribe to `OutBuild.BuildJobs.ArtifactPublished` and use the supplied
recordâ€™s `path`. It fires only after successful publication; callback failures do not invalidate
the build.

## Batch builds / CI

Use the Unity Editor matching the project and an already selected/available build target:

```text
Unity.exe -batchmode -nographics -quit -projectPath "<project>" -buildTarget Android -executeMethod OutBuild.BuildJobs.BuildFromCommandLine -logFile "<build.log>"
```

Environment variables:

| Variable | Meaning |
| --- | --- |
| `OUT_BUILD_PRESET` | Preset name; defaults to `Release` |
| `OUT_BUILD_OUTPUT` | Output override for this CI run; does not change the shared location |
| `OUT_BUILD_SETTINGS_PATH` | Optional alternate shared-settings file for isolated tests/CI |
| `OUT_BUILD_VERSION` | Optional version string; otherwise current project version |
| `OUT_BUILD_CODE` | Optional explicit numeric code; otherwise preset suggestion |
| `OUT_BUILD_KEYSTORE_PASS` | Optional session signing password from CI secret storage |
| `OUT_BUILD_ALIAS_PASS` | Optional session key-alias password from CI secret storage |

CI validates configuration and throws a build failure for invalid setup or an unsuccessful
build, allowing Unity batchmode to report failure. It does not open password dialogs, run
device discovery, deploy to hardware, or export credentials. Signing key paths/aliases and
Android SDK licenses must already be configured in the build environment.

## Verification

From each source project, run `Tools/BuildAutomator.Tests/Run-Checks.ps1` on Windows with the
installed Editor paths (override `-UnityHubEditors` / `-Versions` if needed). It compiles the
standalone source against Unity 2022.3/6 assemblies and runs fake-ADB process tests for device
parsing, argument quoting, output floods, cancellation, timeouts, install and launch failures,
API/ABI mismatch, and TV launcher fallback. Tests never connect to real devices.
The isolated Editor suite also includes `RunMachineSettingsChecks` for migration, concurrent writes,
output precedence, alias deduplication, IP reuse, and selection safety. Set `OUT_BUILD_SETTINGS_PATH`
to an isolated test file; the provided smoke-test launcher does this automatically.

Before release, smoke-test import in a clean Editor, USB authorization, wireless pairing,
Fire TV manual connection, APK build/install/launch, lost Wi-Fi, and failed-build version rollback.
macOS/Linux device behavior and actual hardware require their own smoke tests.

## References

- [Android ADB and wireless debugging](https://developer.android.com/tools/adb)
- [Connect Fire TV through ADB](https://developer.amazon.com/docs/fire-tv/connecting-adb-to-device.html)
- [Unity BuildPipeline.BuildPlayer](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildPipeline.BuildPlayer.html)

Standalone export works from both Dashboard and Full Controls and locates the complete tool folder even after relocation or UPM installation. It exports only that folder and checks that Unity created a nonempty package.
