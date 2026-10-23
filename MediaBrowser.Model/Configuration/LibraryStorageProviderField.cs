namespace MediaBrowser.Model.Configuration;

/// <summary>A storage setting presented to the administrator creating a library.</summary>
public sealed class LibraryStorageProviderField
{
    /// <summary>Gets or sets the setting key sent back to the provider.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the human-readable label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the input type: text, url, password, checkbox, or number.</summary>
    public string InputType { get; set; } = "text";

    /// <summary>Gets or sets an optional explanation of the setting.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the setting is required.</summary>
    public bool Required { get; set; }

    /// <summary>Gets or sets the initial value. Providers must never include stored credentials here.</summary>
    public string? DefaultValue { get; set; }
}
