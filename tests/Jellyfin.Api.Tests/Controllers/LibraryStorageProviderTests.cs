using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Models.LibraryStructureDto;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class LibraryStorageProviderTests
{
    private readonly Mock<ILibraryManager> _libraries = new();
    private readonly Mock<ILibraryStorageProvider> _provider = new();

    [Fact]
    public void Discovery_ReturnsRegisteredProviderSchema()
    {
        var info = new LibraryStorageProviderInfo { Id = "example", Name = "Example storage", Fields = [new() { Key = "container", Label = "Container" }] };
        _provider.Setup(value => value.GetInfo()).Returns(info);

        var result = Controller().GetStorageProviders();

        var providers = Assert.IsAssignableFrom<IEnumerable<LibraryStorageProviderInfo>>(Assert.IsAssignableFrom<OkObjectResult>(result.Result).Value);
        Assert.Same(info, Assert.Single(providers));
    }

    [Fact]
    public async Task Creation_DispatchesProviderSettingsAndStandardLibraryOptions()
    {
        var options = new LibraryOptions();
        var settings = new Dictionary<string, string> { ["container"] = "media" };
        _provider.Setup(value => value.Id).Returns("example");
        _provider.Setup(value => value.CreateLibraryAsync("Movies", CollectionTypeOptions.movies, options, settings, true, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var result = await Controller().AddVirtualFolder(
            "Movies",
            CollectionTypeOptions.movies,
            [],
            new AddVirtualFolderDto { LibraryOptions = options, StorageProviderId = "example", StorageOptions = settings },
            true,
            TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
        _provider.Verify(value => value.CreateLibraryAsync("Movies", CollectionTypeOptions.movies, options, settings, true, TestContext.Current.CancellationToken), Times.Once);
        _libraries.Verify(value => value.AddVirtualFolder(It.IsAny<string>(), It.IsAny<CollectionTypeOptions?>(), It.IsAny<LibraryOptions>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Creation_RejectsUnknownProviderAndMixedSourceKinds()
    {
        _provider.Setup(value => value.Id).Returns("example");
        Assert.IsType<BadRequestObjectResult>(await Controller().AddVirtualFolder("Movies", null, [], new AddVirtualFolderDto { StorageProviderId = "missing" }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.IsType<BadRequestObjectResult>(await Controller().AddVirtualFolder("Movies", null, ["/media"], new AddVirtualFolderDto { StorageProviderId = "example" }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.IsType<BadRequestObjectResult>(await Controller().AddVirtualFolder(
            "Movies",
            null,
            [],
            new AddVirtualFolderDto { StorageProviderId = "example", LibraryOptions = new() { PathInfos = [new("/media")] } },
            cancellationToken: TestContext.Current.CancellationToken));
        _provider.Verify(value => value.CreateLibraryAsync(It.IsAny<string>(), It.IsAny<CollectionTypeOptions?>(), It.IsAny<LibraryOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Creation_PreservesOrdinaryFolderLibraries()
    {
        var options = new LibraryOptions();
        _libraries.Setup(value => value.AddVirtualFolder("Local", null, options, false)).Returns(Task.CompletedTask);

        Assert.IsType<NoContentResult>(await Controller().AddVirtualFolder("Local", null, ["/media"], new AddVirtualFolderDto { LibraryOptions = options }, cancellationToken: TestContext.Current.CancellationToken));

        _libraries.Verify(value => value.AddVirtualFolder("Local", null, options, false), Times.Once);
        Assert.Equal("/media", Assert.Single(options.PathInfos).Path);
    }

    private LibraryStructureController Controller()
        => new(
            Mock.Of<IServerConfigurationManager>(),
            _libraries.Object,
            Mock.Of<ILibraryMonitor>(),
            Mock.Of<IDirectoryService>(),
            Mock.Of<IUserManager>(),
            [_provider.Object]);
}
