# Ads Caller — Package Exporter repair

Completed 2026-09-09 in all four source projects.

The old button gate treated any CoreRelease content-hash difference as a fatal error and logged failures from window redraw. The open SalonAds-CrossPromo project had a modified `AdsMediator.prefab`, which triggered that gate. That prefab edit was preserved, not reset or copied over from another project.

## Behavior

- The default view is compact: current project, export folder, version, three package cards, two collapsed foldouts, and one **Export All Ready** action. Permanent help-box walls were removed.
- Each card has its own editable package name, output filename preview, compact status, and Export button. Names are sanitized safely, the extension is automatic, and each has a recommended-name reset.
- Export folder, version, options, and all three names persist per project in `UserSettings/AdsCallerPackageExporter.json`. In-project folders are stored relative to the project root; the default and reset target are `<Project>/PackageExports`.
- Main is enabled for the installed Android or Amazon provider; CrossPromo is enabled for its own installed provider. Combined requires both. **Export All Ready** exports every currently ready composition and skips unavailable ones. Breaking one provider does not disable the other provider's independent export.
- Local content changes produce visible warnings and an interactive confirmation before export. Current edited contents are exported; the release baseline is not silently rewritten.
- Missing required assets, GUID mismatches, incompatible Core APIs, and invalid manifests remain blockers. Their exact reasons appear inside the window.
- Optional missing demos are skipped. SDK bundling applies only to Main/Combined.
- Less-used toggles are under **Export Options**. Exact blockers and local changes are under **Preflight Details**. Preflight is cached, invalidated by project/option changes and focus, and manually refreshable. Redraw does not flood the Console. Export reruns validation before writing. Compilation/import and Play Mode temporarily disable export.
- Shared synchronization now reads the maintained exporter instead of reconstructing an obsolete version from the original migration backup.

After compilation, reopen **Ads Caller > Package Exporter** or use **Refresh preflight**.

## Verification

Real Unity batch runs in disposable copies of all four source projects passed 92 assertions in total, including the additional combined-provider fixture. Main, CrossPromo, and Combined `.unitypackage` files were actually exported. Archive inspection confirmed that edited prefab contents were included. Tests covered project-relative folder persistence, independently editable package names and versions, positive availability, Export All readiness, missing-provider/asset/SDK cases, optional demos, invalid manifests, GUID/API checks, and quiet repeated preflight.

All four source projects' prefab, configuration, and metadata fingerprints were preserved during these tests. The already-open Unity project was not closed or used for destructive regression checks. Native window clicks were not automated; tests exercised the same preflight consumed by the button conditions. No native-device build or live ad request was performed.

Logs, generated test packages, original-file backups, and `final-results.json` are under the Android project's `Validation/ExporterFix`. The initial Android fixture-copy failure was a Windows path-length issue; its shorter-path retry passed. Earlier release archives remain unchanged; future builds use the corrected source exporter.
