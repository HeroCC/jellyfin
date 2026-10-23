using System;
using System.Collections.Generic;
using MediaBrowser.Model.Configuration;

namespace Jellyfin.Api.Models.LibraryStructureDto;

/// <summary>
/// Update library options dto.
/// </summary>
public class UpdateLibraryOptionsDto
{
    /// <summary>
    /// Gets or sets the library item id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets changes to provider settings. Omitted settings retain their stored values.</summary>
    public Dictionary<string, string> StorageOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets library options.
    /// </summary>
    public LibraryOptions? LibraryOptions { get; set; }
}
