# Ads Caller — naming correction

Completed 2026-09-09 across AdsCaller-Android, TestingAmazonAds, SalonAds-CrossPromo, and SalonAds-CrossPromo-2022.

Ads Caller is the product brand. Filenames, assemblies, menus, windows, prefab names, documentation, and release tools use Ads Caller / AdsCaller. `gss` remains the company qualifier in `com.gss.adscaller.*` package IDs and the package author field, not a product-name prefix.

Examples: `AdsCallerAssets.cs`, `AdsCallerBootstrap.cs`, `AdsCaller.Core.asmdef`, `Ads Caller Main.prefab`, `Ads Caller > Setup Wizard`, and `Tools/adscaller_upm.py`.

## Current release

Use `PackageExports/AdsCaller-UPM/Release-20260909` in the Android source. Package versions are 3.1.1, with CrossPromo 4.1.1 for Unity 6. Package IDs are unchanged, so this is an upgrade of the existing packages, not a second product. The installer and build entry points are `adscaller_upm.py` and `Tools/Build-AdsCaller-UPM.ps1`.

## Checks rerun for this correction

- All four source projects compile and export legacy packages.
- All six clean UPM combinations pass setup, assembly isolation, duplicate rejection, and writable-configuration checks.
- Existing Android+CrossPromo and Amazon+CrossPromo scenes upgrade from the previous named release; references and configuration bytes are preserved.
- Both Combined samples, Unity 2022 CrossPromo, and Core+CrossPromo tarball installation pass.
- All 392 original asset GUIDs, 60 renamed metadata GUIDs, and six original configuration/catalog assets are preserved. Shared source manifests verify in all four projects.
- All eight generated package manifests use Ads Caller display names and unchanged `com.gss.adscaller.*` IDs. Generated package filenames contain no GSS brand prefix.

This correction did not repeat the original 34-assertion Android regression suite, full legacy import-order matrix, negative compilation-error matrix, or native-device ad delivery. Their earlier results remain recorded separately in the migration report.

## Compatibility and recovery

The setup and exporter retain fallback reads for old preference keys. Setup copies an old completion receipt to the new name without overwriting it. Previously created configuration and catalog assets under the old path are reused. GUID-based asset indexes retain aliases for renamed files. Existing Out-prefixed public runtime APIs remain unchanged.

Original naming snapshots and old generated project files are recoverable in `Validation/AdsCaller-Branding/originals` and `Validation/AdsCaller-Branding/obsolete-generated-projects` in the Android source. Eighteen superseded Unity-generated project files were archived after Unity regenerated their AdsCaller-named replacements; no source assets were deleted. Historical validation and prior release directories retain their original names and bytes.

Machine-readable results and logs for these checks are in the Android source's `Validation/AdsCaller-Branding` directory. See `Assets/AdsCaller/AdsCaller-UPM-Guide.md` for installation and compilation-error limitations.
