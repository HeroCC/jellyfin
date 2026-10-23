namespace MediaBrowser.Model.Configuration;

/// <summary>Describes a plugin-provided library storage source and its creation settings.</summary>
public sealed class LibraryStorageProviderInfo
{
    /// <summary>Gets or sets the stable provider identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the settings rendered by library creation clients.</summary>
    public LibraryStorageProviderField[] Fields { get; set; } = [];
}

