namespace Neodash_Mod_Loader.Services;
public sealed record SettingsPageState(string GamePath, string RuntimeStatus, bool HasGame, bool HasRuntimeConfiguration, bool CanRemoveRuntime, bool HasRuntimeLog, bool HasOperationLog, bool HasCrashLog, string? LoadError, Version Version, string? CommitHash);
