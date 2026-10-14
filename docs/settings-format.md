# Settings format

`settings.json` uses UTF-8 JSON and an integer `SchemaVersion`. This version describes the storage contract, independently of the app release number. App releases that keep the same contract continue to use schema **1**.

The source of truth is `Models/AppSettings.cs`. All normal writes go through `SettingsManager.SaveSettings`, the source-generated `AppJsonContext`, and the atomic storage writer. Do not add a second serializer or normalize arrays/objects with `ToString()`.

## Version 1

```json
{
  "SchemaVersion": 1,
  "AppTheme": "Dark",
  "IsSetupComplete": false,
  "ModsTxtMigrated": false,
  "GameDirectory": null,
  "Installations": [],
  "Ue4ssInstallations": [],
  "InstalledMods": {}
}
```

- Property names and value types are part of the contract. Property order and insignificant whitespace are not.
- `Installations` and `Ue4ssInstallations` are arrays of typed records; `InstalledMods` is an object mapping mod names to version strings. They are never JSON encoded inside strings on save.
- Missing or null collections normalize to empty collections. Unknown root fields are retained through `JsonExtensionData`, without interpreting arbitrary string values as JSON.
- Unversioned settings use schema 1. The loader also accepts the three collection fields stringified by version 1.3; it decodes one layer before normal validation. Loading preserves the source file; the next normal save writes canonical schema 1.
- A schema version must be a positive integer supported by the build. Unsupported newer versions, malformed data, and invalid records block writes and preserve the original file. The schema check runs before reading collection contents.

## Future changes

Compatible additions may keep schema 1 if older readers can preserve unknown fields safely. Do not rename persisted keys, change a property's type, or reinterpret ownership/recovery data within the same schema.

For an incompatible change:

1. Increment `AppSettings.CurrentSchemaVersion` and implement an explicit, validated migration from each supported older schema in the load path. An older numeric version is not accepted automatically.
2. Keep fixtures for previous schemas and the 1.3 resave round trip. Add tests proving the migration preserves complete installation, runtime, version, backup, and unknown data.
3. Prove rejection leaves original bytes unchanged, and prove all new writes use the current canonical schema. Include unsupported future-version fixtures so older readers cannot overwrite newer data.
4. Run `eng/Verify.ps1` before publishing.

Profiles and package backup manifests have separate persistence contracts; changing settings does not silently migrate those files.
