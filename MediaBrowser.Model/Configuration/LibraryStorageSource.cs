using System;

namespace MediaBrowser.Model.Configuration;

/// <summary>Identifies the plugin-managed storage associated with a native library.</summary>
public sealed class LibraryStorageSource
{
    /// <summary>Gets or sets the stable identifier of the owning storage provider.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Gets or sets the server-generated identifier of this storage source.</summary>
    /// <remarks>
    /// Providers persist their configuration and credentials separately, keyed by this identifier.
    /// This reference contains no credentials and survives library renames and server restarts.
    /// </remarks>
    public Guid SourceId { get; set; }
}
