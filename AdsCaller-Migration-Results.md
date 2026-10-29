# Ads Caller migration — completed

Date: 2026-09-09

Implemented across AdsCaller-Android, TestingAmazonAds, SalonAds-CrossPromo, and SalonAds-CrossPromo-2022.

## Delivered

- Separate Ads Caller Core, Android, Amazon, CrossPromo, and Editor assemblies. The editor bootstrap has no runtime, SDK, or Assembly-CSharp assembly references.
- Real UPM packages with declared mandatory dependencies, local installation tooling, and `.tgz` distribution. Private Ads Caller packages are supplied locally; no registry was published.
- CrossPromo 3.1.0 for Unity 2022.3 / Addressables 1.29.0, and CrossPromo 4.1.0 for Unity 6 / Addressables 3.1.0. Both declare Newtonsoft JSON 3.2.2 and Input System 1.7.0.
- Ads Caller menus and package branding, GUID-based asset lookup, preserved legacy public API names, writable project configuration, and structural setup verification.
- Main/CrossPromo bootstrap prefabs, optional Android/Amazon Combined UPM samples, and a bootstrap creation menu.
- Separate optional Firebase and AppsFlyer adapters; mandatory dependency installation removed from the wizard.
- Legacy exports and a GUID-based migration installer with backups and rollback on failure.
- Updated integration documentation and a root README in every source project.

## Verification

This table records the original architecture migration tests. See [branding correction checks](AdsCaller-Branding-Results.md) for checks rerun on the corrected naming revision.

| Check | Result |
| --- | --- |
| Four source projects compile and export | Passed |
| Android regression suite | 34 assertions passed |
| Six clean UPM installation combinations | Passed |
| Existing-scene package upgrades | Passed |
| Main then CrossPromo / CrossPromo then Main | Passed |
| Dependencies resolve while a deliberate game-script error exists | Passed |
| Setup after repairing the game-script error | Passed |
| Legacy configuration and scene preservation | Passed |
| Legacy import/reimport in both orders and Combined re-export | Passed |
| Final Android/Amazon Combined samples and Unity 2022 CrossPromo | Passed |
| Core + CrossPromo installation from tarballs | Passed |
| Original asset/script GUIDs | 392 checked and preserved |
| Configuration/catalog assets | 6 unchanged byte-for-byte |

The deliberate broken-game log contains an expected compiler error. It is an intentional negative test, followed by a passing recovery test. Full logs and machine-readable results are in this directory.

## Release and use

The current release is `PackageExports/AdsCaller-UPM/Release-20260909` in the Android source workspace (3.1.1 packages, with CrossPromo 4.1.1 for Unity 6). Read its README or the project-specific `Assets/AdsCaller/AdsCaller-UPM-Guide.md` before installing. The source projects remain editable Assets-based release workspaces; the generated packages are the UPM distribution. Earlier release directories remain unchanged as historical artifacts.

Close the destination editor and use the release's `adscaller_upm.py install` command to add the complete local dependency set. UPM then resolves public Unity packages before the dependent assemblies compile. After compilation, open **Ads Caller > Setup Wizard**.

Compilation isolation does not bypass unrelated game errors or Unity Safe Mode. Safe Mode cannot run project/package editor code or support prefab editing. No native device ad-delivery test or new Android device build was performed for this packaging migration.

## Backups and compatibility

Original source assets remain in `Validation/GSS-Migration/originals/<project>`. Provider scripts moved with their metadata. Existing configuration values and prefab references were retained. The historical Out-prefixed public interfaces/enums and independent Build Automator implementation keep their identities for compatibility; the product name is Ads Caller; gss is the package identifier company qualifier.

Consumer migrations archive overlapping legacy implementation assets in that project's `AdsCallerMigrationBackups` directory. Existing project configuration keeps its GUID, and package templates receive separate GUIDs. Modified legacy scripts are archived for manual porting into extensions or a maintained package fork.
