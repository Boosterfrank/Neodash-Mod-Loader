# Profile format

`profiles.json` and portable profile exports share a UTF-8 JSON envelope with an integer `schemaVersion`. This contract is independent of the app release and settings schema. Compatible app releases keep profile schema **1**.

The models are in `Models/ModProfile.cs`. `ProfileJsonFormat` checks versions before materializing profiles and uses the source-generated `AppJsonContext` for serialization. `ProfileManager` validates profile identities and game bindings, then writes atomically. Use these paths for every profile load, save, import, and export.

## Version 1

```json
{
  "schemaVersion": 1,
  "activeProfile": "Default",
  "profiles": [
    {
      "name": "Default",
      "enabledMods": ["ExampleMod"]
    }
  ]
}
```

- Preserve the existing camelCase names and value types. `profiles` and `enabledMods` are native arrays, never JSON strings. Property order and insignificant whitespace are not part of the contract.
- Each profile has a unique name (case insensitive, at most 40 characters) and an enabled folder-name set. Existing trimming, case-insensitive deduplication, and sorting remain in effect. Selections for unavailable mods are retained.
- An optional absolute `gamePath` binds a local profile to its Neodash Win64 folder. An absent or null path means a portable/unbound profile. Local migrations retain existing bindings and do not invent bindings for older profiles.
- Unknown properties are retained at both the envelope and individual-profile levels, including nested JSON, nulls, and strings that happen to contain JSON.
- Original 1.3 profiles and earlier unversioned 1.4 profiles use schema 1. Loading validates and migrates in memory without rewriting the source; the next normal save adds `schemaVersion: 1`. Names, active selection, enabled selections, game bindings, and unknown fields are preserved.
- Empty/missing/null profile lists with no active selection keep the existing default-profile behavior. Missing/null enabled lists become empty sets. Invalid identities, ambiguous duplicates, malformed data, and unsupported versions block local writes and preserve the original file.
- Schema checks run before reading profile contents. Versions must be positive integers supported by the build. A future version explains that a newer loader is required; it cannot be saved or imported by an older reader.

## Portable imports and exports

Exports contain exactly the active profile, plus retained envelope and profile metadata. They omit the local `gamePath` deliberately. Imports also clear that binding, so sharing a profile never associates it with another user's installation. Neither operation changes game files.

Imports retain the existing profile list and active selection. Unknown envelope metadata is merged without overwriting existing values; a conflicting value rejects the import and leaves both files unchanged. Failed saves roll back both the added profile and merged metadata. Exports cannot target the loader's own `profiles.json`, which would otherwise discard inactive profiles.

## Future changes

1. Keep schema 1 for compatible additions only when older readers can preserve them safely. Do not rename keys, change types, or reinterpret selections or bindings in the same schema.
2. For an incompatible change, increment `ModProfileData.CurrentSchemaVersion` and implement an explicit migration from every supported older schema in `ProfileJsonFormat`. The unversioned baseline remains **1**; it must never follow the current-version constant automatically.
3. Retain legacy fixtures. Verify complete profile data, unknown metadata, portable import/export, write-failure rollback, and byte-preserving rejection of unsupported versions. A newer schema must not bypass existing validation.
4. Run `eng/Verify.ps1` before publishing.

An old executable that discards fields it does not recognize cannot recover those fields when it saves. Keep backups before downgrading; the current reader preserves the data present in the file it receives.
