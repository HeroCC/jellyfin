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
    /// <param name="name">The library display name.</param>
    /// <param name="collectionType">The native library content type, or null for mixed content.</param>
    /// <param name="options">Standard library options. The provider supplies its managed paths.</param>
    /// <param name="settings">Administrator-supplied provider settings.</param>
    /// <param name="refreshLibrary">Whether to start scanning the new library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing library creation.</returns>
    Task CreateLibraryAsync(string name, CollectionTypeOptions? collectionType, LibraryOptions options,
        IReadOnlyDictionary<string, string> settings, bool refreshLibrary, CancellationToken cancellationToken);
}
