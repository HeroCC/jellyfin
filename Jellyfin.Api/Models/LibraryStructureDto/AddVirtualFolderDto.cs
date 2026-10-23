using System.Collections.Generic;
using MediaBrowser.Model.Configuration;

namespace Jellyfin.Api.Models.LibraryStructureDto;

/// <summary>
/// Add virtual folder dto.
/// </summary>
public class AddVirtualFolderDto
{
    /// <summary>Gets or sets the optional plugin storage provider identifier.</summary>
    public string? StorageProviderId { get; set; }

    /// <summary>Gets or sets storage-specific creation settings.</summary>
    public Dictionary<string, string> StorageOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets library options.
    /// </summary>
    public LibraryOptions? LibraryOptions { get; set; }
}
