# Ad Call Migration — validation

Implemented in AdsCaller-Android, TestingAmazonAds, SalonAds-CrossPromo, and SalonAds-CrossPromo-2022.

- Shared editor sources and Unity metadata are byte-identical across all four projects.
- Each project compiled in its installed Unity editor and passed **40 assertions**. The first three used Unity 6000.0.79f1; the final project used Unity 2022.3.62f3. Every batch editor exited with code 0.
- Tests cover 50 calls grouped into one manager, arbitrary/misspelled names, aliases, typed variables, unresolved singleton types, overloads, comments/strings, multiline arguments, active defines, callback preservation (including reordered named arguments and lambdas), blocked value/conditional contexts, compiler preflight, stale-file rejection, encoding preservation, and backup/restore.
- The production window's scan path was exercised against each project's active player assemblies.
- The UPM builder successfully generated all eight package variants in the Android project's `Validation/AdCallMigration/UPM-final`. The shared core contains the migration window, engine, parser bridge, documentation and metadata.
- Core release hashes were updated only for the feature's shared files. Existing unrelated baselines were preserved.
- Existing Android Legacy/DOTween warnings remain. No C# compiler errors occurred.

Open **Ads Caller > Ad Call Migration**, or the **Migrate old ad manager calls** foldout in the Setup Wizard. The feature scans the active target's C# branches; scene/prefab bindings and DLL internals are outside its migration scope. No real gameplay ad calls were migrated as part of installing the feature.

The per-project evidence is in `assertions.txt` and `unity-validation-final.log` alongside this report. Original edited files are retained in `Originals`. The UI was compiled and its scan path tested in batch mode; a graphical screenshot review was not performed.
