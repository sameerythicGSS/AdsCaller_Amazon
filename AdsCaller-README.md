# Ads Caller — TestingAmazonAds

Start with [installation and migration](Assets/AdsCaller/AdsCaller-UPM-Guide.md).

This is an editable release source project. Its current Assets layout and metadata are preserved for legacy exports; the distribution builder generates UPM packages with separate Core, provider, and editor assemblies.

Open **Ads Caller > Setup Wizard** after compilation. UPM resolves required packages before this window loads. The wizard verifies scene components and creates writable configuration assets for UPM users.

Source changes and new tooling are documented in the shared guide. Original assets for this migration are retained in the Android source under `Validation/GSS-Migration/originals/TestingAmazonAds`. Test reports are under the Android source's `Validation/GSS-Migration` directory.

From the Android source workspace, run `python Tools/adscaller_upm.py build` to produce a release containing all four modules and both CrossPromo compatibility lines. No registry publication is performed. The generated installer works without Unity compilation.

See [completed migration and verification](AdsCaller-Migration-Results.md) for the delivered release and test results.

See [naming correction and current release](AdsCaller-Branding-Results.md) for the naming policy and checks rerun for this revision.

See [Package Exporter repair and verification](AdsCaller-Exporter-Fix.md) for availability rules and validation results.
