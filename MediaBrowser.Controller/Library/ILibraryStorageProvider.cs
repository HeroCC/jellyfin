using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Library;

/// <summary>Creates native media libraries backed by plugin-managed storage.</summary>
public interface ILibraryStorageProvider
{
    /// <summary>Gets the stable identifier used to select this storage provider.</summary>
    string Id { get; }

    /// <summary>Describes creation settings without exposing stored secrets.</summary>
    /// <returns>The provider and its input fields.</returns>
    LibraryStorageProviderInfo GetInfo();

    /// <summary>Provisions storage and registers a native Jellyfin library.</summary>
    /// <remarks>
    /// The server supplies <see cref="LibraryOptions.StorageSource"/> before calling this method.
    /// Persist provider configuration and credentials separately under its source identifier,
    /// and pass these options, including the storage reference, to the native library manager.
    /// Roll back provisioned resources and configuration if creation fails.
    /// </remarks>
    /// <param name="name">The library display name.</param>
    /// <param name="collectionType">The native library content type, or null for mixed content.</param>
    /// <param name="options">Standard library options. The provider supplies its managed paths.</param>
    /// <param name="settings">Administrator-supplied provider settings.</param>
    /// <param name="refreshLibrary">Whether to start scanning the new library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing library creation.</returns>
    Task CreateLibraryAsync(
        string name,
        CollectionTypeOptions? collectionType,
        LibraryOptions options,
        IReadOnlyDictionary<string, string> settings,
        bool refreshLibrary,
        CancellationToken cancellationToken);

    /// <summary>Updates storage before the server saves the native library options.</summary>
    /// <remarks>
    /// Load configuration using the source identifier. Settings contain only submitted changes;
    /// omitted settings, including credentials, must retain their stored values.
    /// Keep the existing managed paths stable while reconfiguring their backing storage.
    /// Do not save the native library here. Throwing prevents the server from saving its options.
    /// Updates must tolerate retries if saving the native library subsequently fails.
    /// </remarks>
    /// <param name="source">The persisted storage reference.</param>
    /// <param name="settings">Administrator-supplied changes to provider settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing storage configuration updates.</returns>
    Task UpdateLibraryAsync(
        LibraryStorageSource source,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken);

    /// <summary>Releases managed storage resources before the server unregisters a native library.</summary>
    /// <remarks>
    /// Stop synchronization and release mounts, caches and stored configuration associated with
    /// the source. Do not delete remote media or unregister the native library here.
    /// Cleanup must be idempotent: unregistering can fail after cleanup and the server may retry.
    /// Throwing leaves the native library registered so cleanup can be retried.
    /// </remarks>
    /// <param name="source">The persisted storage reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing storage cleanup.</returns>
    Task RemoveLibraryAsync(LibraryStorageSource source, CancellationToken cancellationToken);
}
