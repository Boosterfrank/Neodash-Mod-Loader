using Xunit;
using static Neodash.ModLoader.Integration.IntegrationTestHost;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Neodash.ModLoader.Integration;

[CollectionDefinition("Integration process tests", DisableParallelization = true)]
public sealed class IntegrationProcessCollection { }

[Collection("Integration process tests")]
public sealed partial class IntegrationTests
{
    [Fact] public Task SerilogWritesReadableLocalLogsAndCrashDetails() => AssertIsolatedAsync(nameof(SerilogWritesReadableLocalLogsAndCrashDetails), scratch => { SerilogFileLogging(scratch); return Task.CompletedTask; });
    [Fact] public Task SerilogRotationPreservesLegacyLogsAndOpensNewestFile() => AssertIsolatedAsync(nameof(SerilogRotationPreservesLegacyLogsAndOpensNewestFile), scratch => { SerilogRotationAndLegacyLogs(scratch); return Task.CompletedTask; });
    [Fact] public Task SerilogSupportsSharedFileWriters() => AssertIsolatedAsync(nameof(SerilogSupportsSharedFileWriters), scratch => { SerilogSharedWriters(scratch); return Task.CompletedTask; });
    [Fact] public Task SerilogRetriesTransientInitializationFailures() => AssertIsolatedAsync(nameof(SerilogRetriesTransientInitializationFailures), scratch => { SerilogInitializationRecovery(scratch); return Task.CompletedTask; });

    [Fact] public Task NativeAndLuaModEntryPointsAreDisplayed() => AssertIsolatedAsync(nameof(NativeAndLuaModEntryPointsAreDisplayed), NativeAndLuaModEntryPointsAreDisplayedCore);
    [Fact] public Task NativeModUpdateRemovesObsoleteDllsAndRestoresBackup() => AssertIsolatedAsync(nameof(NativeModUpdateRemovesObsoleteDllsAndRestoresBackup), NativeModUpdateRemovesObsoleteDllsAndRestoresBackupCore);
    [Fact] public Task Ue4SsUpdateRemovesOnlyObsoleteOwnedRuntimeFiles() => AssertIsolatedAsync(nameof(Ue4SsUpdateRemovesOnlyObsoleteOwnedRuntimeFiles), Ue4SsUpdateRemovesOnlyObsoleteOwnedRuntimeFilesCore);
    [Fact] public Task Ue4SsObsoleteRuntimeCleanupRollsBackOnCommitFailure() => AssertIsolatedAsync(nameof(Ue4SsObsoleteRuntimeCleanupRollsBackOnCommitFailure), Ue4SsObsoleteRuntimeCleanupRollsBackOnCommitFailureCore);
    [Fact] public Task Ue4SsCancelledCleanupRestoresDeletedDlls() => AssertIsolatedAsync(nameof(Ue4SsCancelledCleanupRestoresDeletedDlls), Ue4SsCancelledCleanupRestoresDeletedDllsCore);
    [Fact] public Task Ue4SsObsoleteRuntimeCleanupRejectsUnsafeOrLockedFiles() => AssertIsolatedAsync(nameof(Ue4SsObsoleteRuntimeCleanupRejectsUnsafeOrLockedFiles), Ue4SsObsoleteRuntimeCleanupRejectsUnsafeOrLockedFilesCore);
    [Fact] public Task Ue4SsUninstallRestoresDistinctDllPathsOnFailure() => AssertIsolatedAsync(nameof(Ue4SsUninstallRestoresDistinctDllPathsOnFailure), Ue4SsUninstallRestoresDistinctDllPathsOnFailureCore);

    [Fact] public Task PackageInstallRestoreAndConfiguration() => AssertIsolatedAsync(nameof(PackageInstallRestoreAndConfiguration), scratch => { InstallRestoreAndConfiguration(scratch); return Task.CompletedTask; });
    [Fact] public Task RenamedPackageStateAndRestore() => AssertIsolatedAsync(nameof(RenamedPackageStateAndRestore), scratch => { RenamedPackageAndRestore(scratch); return Task.CompletedTask; });
    [Fact] public Task ZipLayoutsAndArchiveValidation() => AssertIsolatedAsync(nameof(ZipLayoutsAndArchiveValidation), scratch => { ZipLayoutsAndUnsupportedArchive(scratch); return Task.CompletedTask; });
    [Fact] public Task PermanentUninstallAndLockedRecoveryFact() => AssertIsolatedAsync(nameof(PermanentUninstallAndLockedRecoveryFact), scratch => { PermanentUninstallAndLockedRecovery(scratch); return Task.CompletedTask; });
    [Fact] public Task UnsafeSnapshotsNamesAndJunctions() => AssertIsolatedAsync(nameof(UnsafeSnapshotsNamesAndJunctions), scratch => { UnsafeSnapshotsAndFolders(scratch); return Task.CompletedTask; });
    [Fact] public Task ModManagerEnableMigrationAndRollback() => AssertIsolatedAsync(nameof(ModManagerEnableMigrationAndRollback), scratch => { ModManagerBehavior(scratch); return Task.CompletedTask; });
    [Fact] public Task ProfileCrudImportExportAndValidation() => AssertIsolatedAsync(nameof(ProfileCrudImportExportAndValidation), scratch => { ProfileBehavior(scratch); return Task.CompletedTask; });
    [Fact] public Task UnversionedProfilesMigrateWithoutDataLoss() => AssertIsolatedAsync(nameof(UnversionedProfilesMigrateWithoutDataLoss), scratch => { LegacyProfileMigration(scratch); return Task.CompletedTask; });
    [Fact] public Task VersionedProfileImportExportPreservesMetadata() => AssertIsolatedAsync(nameof(VersionedProfileImportExportPreservesMetadata), scratch => { ProfileMetadataRoundTrip(scratch); return Task.CompletedTask; });
    [Fact] public Task ProfileImportFailurePreservesProfilesAndMetadata() => AssertIsolatedAsync(nameof(ProfileImportFailurePreservesProfilesAndMetadata), scratch => { ProfileImportRollback(scratch); return Task.CompletedTask; });
    [Fact] public Task UnsupportedProfileSchemasCannotBeImported() => AssertIsolatedAsync(nameof(UnsupportedProfileSchemasCannotBeImported), scratch => { ProfileImportSchemaGuard(scratch); return Task.CompletedTask; });
    [Fact] public Task CatalogPathSafetyAndGameDiscovery() => AssertIsolatedAsync(nameof(CatalogPathSafetyAndGameDiscovery), scratch => { CatalogAndPathBehavior(scratch); return Task.CompletedTask; });
    [Fact] public Task SemanticVersionOrdering() => AssertIsolatedAsync(nameof(SemanticVersionOrdering), _ => { VersionOrdering(); return Task.CompletedTask; });
    [Fact] public Task FileMoveRetriesRecoverTransientLocksAndPreserveFailures() => AssertIsolatedAsync(nameof(FileMoveRetriesRecoverTransientLocksAndPreserveFailures), _ => { FileMoveRetryBehavior(); return Task.CompletedTask; });
    [Fact] public Task SettingsJsonCompatibility() => AssertIsolatedAsync(nameof(SettingsJsonCompatibility), _ => { SettingsJsonCompatibilityCore(); return Task.CompletedTask; });
    [Fact] public Task OldVersionSettingsResaveRemainsCompatible() => AssertIsolatedAsync(nameof(OldVersionSettingsResaveRemainsCompatible), scratch => { OldVersionSettingsResaveCompatibility(scratch); return Task.CompletedTask; });
    [Fact] public Task InvalidLegacyCollectionSettingsArePreserved() => AssertIsolatedAsync(nameof(InvalidLegacyCollectionSettingsArePreserved), scratch => { InvalidLegacyCollectionSettings(scratch); return Task.CompletedTask; });
    [Fact] public Task MixedAndEmptySettingsFormatsRemainCompatible() => AssertIsolatedAsync(nameof(MixedAndEmptySettingsFormatsRemainCompatible), _ => { MixedAndEmptySettingsFormats(); return Task.CompletedTask; });
    [Fact] public Task SettingsWritesUseVersionedCanonicalFormat() => AssertIsolatedAsync(nameof(SettingsWritesUseVersionedCanonicalFormat), _ => { SettingsWriteContract(); return Task.CompletedTask; });
    [Fact] public Task UnsupportedSettingsSchemasArePreserved() => AssertIsolatedAsync(nameof(UnsupportedSettingsSchemasArePreserved), _ => { UnsupportedSettingsSchemas(); return Task.CompletedTask; });
    [Fact] public Task SettingsSaveRejectsUnsupportedSchemas() => AssertIsolatedAsync(nameof(SettingsSaveRejectsUnsupportedSchemas), _ => { SettingsSaveSchemaGuard(); return Task.CompletedTask; });
    [Fact] public Task LegacySettingsMigrationKeepsProfilesAndSettingsCompatible() => IsLegacyMigrationChild() ? RunLegacyMigrationChildAsync() : AssertIsolatedAsync(nameof(LegacySettingsMigrationKeepsProfilesAndSettingsCompatible), LegacySettingsMigrationCompatibility);
    [Fact] public Task OperationCancellationAndRecoveryFailures() => AssertIsolatedAsync(nameof(OperationCancellationAndRecoveryFailures), _ => { OperationFailureSemantics(); return Task.CompletedTask; });
    [Fact] public Task CorruptSettingsArePreserved() => AssertIsolatedAsync(nameof(CorruptSettingsArePreserved), _ => { SettingsCorruptionIsPreserved(); return Task.CompletedTask; });
    [Fact] public Task SemanticProfileCorruptionIsPreservedFact() => IsCorruptProfileChild() ? RunCorruptProfileChildAsync() : AssertIsolatedAsync(nameof(SemanticProfileCorruptionIsPreservedFact), SemanticProfileCorruptionIsPreserved);
    [Fact] public Task NestedInvalidSettingsArePreserved() => AssertIsolatedAsync(nameof(NestedInvalidSettingsArePreserved), _ => { SettingsNestedInvalidityIsPreserved(); return Task.CompletedTask; });
    [Fact] public Task ConfigurationEncodingAndTransactionalWrites() => AssertIsolatedAsync(nameof(ConfigurationEncodingAndTransactionalWrites), scratch => { ConfigurationEncodingAndTransactions(scratch); return Task.CompletedTask; });
    [Fact] public Task GameProcessMatchingIsInstallationSpecific() => AssertIsolatedAsync(nameof(GameProcessMatchingIsInstallationSpecific), scratch => { GameProcessMatching(scratch); return Task.CompletedTask; });
    [Fact] public Task CancelledInstallLeavesGameUnchanged() => AssertIsolatedAsync(nameof(CancelledInstallLeavesGameUnchanged), scratch => { CancelledInstall(scratch); return Task.CompletedTask; });
    [Fact] public Task CancelledUpdatePreservesExistingModAndCleansSnapshots() => AssertIsolatedAsync(nameof(CancelledUpdatePreservesExistingModAndCleansSnapshots), scratch => { CancelledUpdatePreservesExistingMod(scratch); return Task.CompletedTask; });
    [Fact] public Task ServiceTransactionsAreAtomic() => AssertIsolatedAsync(nameof(ServiceTransactionsAreAtomic), ServiceTransactions);
    [Fact] public Task LateGameStartAbortsMutationsFact() => AssertIsolatedAsync(nameof(LateGameStartAbortsMutationsFact), LateGameStartAbortsMutations);
    [Fact] public Task ModStateChangesRollBackWhenProfilePersistenceFails() => AssertIsolatedAsync(nameof(ModStateChangesRollBackWhenProfilePersistenceFails), scratch => { ModStatePersistenceRollback(scratch); return Task.CompletedTask; });
    [Fact] public Task CatalogOwnershipNeverRemovesAnotherPackage() => AssertIsolatedAsync(nameof(CatalogOwnershipNeverRemovesAnotherPackage), CatalogOwnershipIsolation);
    [Fact] public Task ModsListSnapshotsPreserveBytesAndUnrelatedEntries() => AssertIsolatedAsync(nameof(ModsListSnapshotsPreserveBytesAndUnrelatedEntries), scratch => { ModsListSnapshotTextFidelity(scratch); return Task.CompletedTask; });
    [Fact] public Task ProfileSyncPreservesUnavailableSelectionsFact() => AssertIsolatedAsync(nameof(ProfileSyncPreservesUnavailableSelectionsFact), scratch => { ProfileSyncPreservesUnavailableSelections(scratch); return Task.CompletedTask; });
    [Fact] public Task PackageFolderRenamesReconcileBoundProfilesTransactionally() => AssertIsolatedAsync(nameof(PackageFolderRenamesReconcileBoundProfilesTransactionally), scratch => { PackageFolderReconciliation(scratch); return Task.CompletedTask; });
    [Fact] public Task Ue4SsArchiveInstallPreservesUserFilesAndTracksPackage() => AssertIsolatedAsync(nameof(Ue4SsArchiveInstallPreservesUserFilesAndTracksPackage), Ue4SsArchiveInstallPreservesUserFilesAndTracksPackageCore);
    [Fact] public Task Ue4SsArchiveInstallRollsBackAfterSettingsFailure() => AssertIsolatedAsync(nameof(Ue4SsArchiveInstallRollsBackAfterSettingsFailure), Ue4SsArchiveInstallRollsBackAfterSettingsFailureCore);
    [Fact] public Task Ue4SsUninstallRestoresOnCommitFailureAndPreservesCustomFiles() => AssertIsolatedAsync(nameof(Ue4SsUninstallRestoresOnCommitFailureAndPreservesCustomFiles), Ue4SsUninstallRestoresOnCommitFailureAndPreservesCustomFilesCore);
    [Fact] public Task Ue4SsStatusDetectsVersionFromMetadataOrStartupHeader() => AssertIsolatedAsync(nameof(Ue4SsStatusDetectsVersionFromMetadataOrStartupHeader), Ue4SsStatusDetectsVersionFromMetadataOrStartupHeaderCore);
    [Fact] public Task Ue4SsStatusIgnoresMissingStaleLockedAndInvalidLogs() => AssertIsolatedAsync(nameof(Ue4SsStatusIgnoresMissingStaleLockedAndInvalidLogs), Ue4SsStatusIgnoresMissingStaleLockedAndInvalidLogsCore);
    [Fact] public Task Ue4SsStatusDetectsFutureVersionNumbersAndNestedLayouts() => AssertIsolatedAsync(nameof(Ue4SsStatusDetectsFutureVersionNumbersAndNestedLayouts), Ue4SsStatusDetectsFutureVersionNumbersAndNestedLayoutsCore);
    [Fact] public Task CatalogAvailabilityReflectsDiskAndTrackedPackageState() => AssertIsolatedAsync(nameof(CatalogAvailabilityReflectsDiskAndTrackedPackageState), scratch => { CatalogAvailability(scratch); return Task.CompletedTask; });
    [Fact] public Task RuntimeAndLogAvailabilityReflectsExistingFilesOnly() => AssertIsolatedAsync(nameof(RuntimeAndLogAvailabilityReflectsExistingFilesOnly), scratch => { RuntimeAndLogAvailability(scratch); return Task.CompletedTask; });
    [Fact] public Task LuaCreationPreservesTransactionalState() => AssertIsolatedAsync(nameof(LuaCreationPreservesTransactionalState), LuaCreationCore);
    [Fact] public Task LuaCreationRejectsConflictsAndLateMutations() => AssertIsolatedAsync(nameof(LuaCreationRejectsConflictsAndLateMutations), LuaCreationRefusalsCore);
    [Fact] public Task LuaEditorFoldersAndProcessArgumentsAreSafe() => AssertIsolatedAsync(nameof(LuaEditorFoldersAndProcessArgumentsAreSafe), scratch => { LuaEditorCore(scratch); return Task.CompletedTask; });
}
