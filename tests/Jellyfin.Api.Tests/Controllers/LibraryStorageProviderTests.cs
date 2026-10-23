using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Models.LibraryStructureDto;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class LibraryStorageProviderTests
{
    private readonly Mock<ILibraryManager> _libraries = new();
    private readonly Mock<ILibraryStorageProvider> _provider = new();
    private readonly Mock<IServerConfigurationManager> _configuration = new();

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
        Assert.Equal("example", options.StorageSource!.ProviderId);
        Assert.NotEqual(Guid.Empty, options.StorageSource.SourceId);
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

    [Fact]
    public void StorageReference_SurvivesLibraryOptionsSerialization()
    {
        var source = new LibraryStorageSource { ProviderId = "webdav", SourceId = Guid.NewGuid() };
        var serializer = new XmlSerializer(typeof(LibraryOptions));
        using var stream = new MemoryStream();
        serializer.Serialize(stream, new LibraryOptions { StorageSource = source });
        stream.Position = 0;

        var restored = Assert.IsType<LibraryOptions>(serializer.Deserialize(stream));

        Assert.Equal(source.ProviderId, restored.StorageSource!.ProviderId);
        Assert.Equal(source.SourceId, restored.StorageSource.SourceId);
    }

    [Fact]
    public async Task Creation_RejectsClientAssignedStorageReference()
    {
        var result = await Controller().AddVirtualFolder(
            "Movies",
            null,
            [],
            new AddVirtualFolderDto { LibraryOptions = new() { StorageSource = new() { ProviderId = "example", SourceId = Guid.NewGuid() } } },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        _libraries.Verify(value => value.AddVirtualFolder(It.IsAny<string>(), It.IsAny<CollectionTypeOptions?>(), It.IsAny<LibraryOptions>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Removal_CleansUpStorageBeforeUnregisteringLibrary()
    {
        var source = ConfigureManagedLibrary();
        var cleanedUp = false;
        _provider.Setup(value => value.RemoveLibraryAsync(source, TestContext.Current.CancellationToken))
            .Callback(() => cleanedUp = true)
            .Returns(Task.CompletedTask);
        _libraries.Setup(value => value.RemoveVirtualFolder("Movies", true))
            .Callback(() => Assert.True(cleanedUp))
            .Returns(Task.CompletedTask);

        var result = await Controller().RemoveVirtualFolder("Movies", true, TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
        _provider.Verify(value => value.RemoveLibraryAsync(source, TestContext.Current.CancellationToken), Times.Once);
        _libraries.Verify(value => value.RemoveVirtualFolder("Movies", true), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removal_PreservesLibraryWhenProviderIsUnavailableOrCleanupFails(bool providerAvailable)
    {
        var source = ConfigureManagedLibrary();
        if (providerAvailable)
        {
            _provider.Setup(value => value.RemoveLibraryAsync(source, TestContext.Current.CancellationToken)).ThrowsAsync(new IOException("Cleanup failed."));
            await Assert.ThrowsAsync<IOException>(() => Controller().RemoveVirtualFolder("Movies", cancellationToken: TestContext.Current.CancellationToken));
        }
        else
        {
            _provider.Setup(value => value.Id).Returns("other");
            Assert.IsType<ConflictObjectResult>(await Controller().RemoveVirtualFolder("Movies", cancellationToken: TestContext.Current.CancellationToken));
        }

        _libraries.Verify(value => value.RemoveVirtualFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Removal_PreservesOrdinaryLibraryBehavior()
    {
        _libraries.Setup(value => value.GetVirtualFolders()).Returns([new() { Name = "Local", LibraryOptions = new() }]);
        _libraries.Setup(value => value.RemoveVirtualFolder("Local", false)).Returns(Task.CompletedTask);

        Assert.IsType<NoContentResult>(await Controller().RemoveVirtualFolder("Local", cancellationToken: TestContext.Current.CancellationToken));

        _libraries.Verify(value => value.RemoveVirtualFolder("Local", false), Times.Once);
        _provider.Verify(value => value.RemoveLibraryAsync(It.IsAny<LibraryStorageSource>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Removal_CanRetryWhenUnregisteringFailsAfterCleanup()
    {
        var source = ConfigureManagedLibrary();
        _provider.Setup(value => value.RemoveLibraryAsync(source, TestContext.Current.CancellationToken)).Returns(Task.CompletedTask);
        _libraries.SetupSequence(value => value.RemoveVirtualFolder("Movies", false))
            .ThrowsAsync(new IOException("Library is busy."))
            .Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<IOException>(() => Controller().RemoveVirtualFolder("Movies", cancellationToken: TestContext.Current.CancellationToken));
        Assert.IsType<NoContentResult>(await Controller().RemoveVirtualFolder("Movies", cancellationToken: TestContext.Current.CancellationToken));

        _provider.Verify(value => value.RemoveLibraryAsync(source, TestContext.Current.CancellationToken), Times.Exactly(2));
        _libraries.Verify(value => value.RemoveVirtualFolder("Movies", false), Times.Exactly(2));
    }

    [Fact]
    public async Task Removal_CancellationDoesNotUnregisterLibrary()
    {
        var source = ConfigureManagedLibrary();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _provider.Setup(value => value.RemoveLibraryAsync(source, cancellation.Token)).Returns(Task.FromCanceled(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Controller().RemoveVirtualFolder("Movies", cancellationToken: cancellation.Token));

        _libraries.Verify(value => value.RemoveVirtualFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Removal_LoadsPersistedStorageReferenceBeforeLibraryIsScanned()
    {
        var source = new LibraryStorageSource { ProviderId = "example", SourceId = Guid.NewGuid() };
        using var library = new LibraryOptionsScope(new() { StorageSource = source });
        var name = Path.GetFileName(library.Folder.Path);
        _configuration.Setup(value => value.ApplicationPaths.DefaultUserViewsPath).Returns(Path.GetDirectoryName(library.Folder.Path)!);
        _libraries.Setup(value => value.GetVirtualFolders()).Returns([new() { Name = name }]);
        _provider.Setup(value => value.Id).Returns("example");
        _provider.Setup(value => value.RemoveLibraryAsync(source, TestContext.Current.CancellationToken)).Returns(Task.CompletedTask);
        _libraries.Setup(value => value.RemoveVirtualFolder(name, false)).Returns(Task.CompletedTask);
        Directory.CreateDirectory(library.Folder.Path);
        try
        {
            Assert.IsType<NoContentResult>(await Controller().RemoveVirtualFolder(name, cancellationToken: TestContext.Current.CancellationToken));
            _provider.Verify(value => value.RemoveLibraryAsync(source, TestContext.Current.CancellationToken), Times.Once);
        }
        finally
        {
            Directory.Delete(library.Folder.Path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_UsesPersistedIdentityAndProviderPathsBeforeSavingOptions(bool supplyForgedReference)
    {
        var source = ConfigureManagedLibrary();
        using var library = new LibraryOptionsScope(new() { StorageSource = source, PathInfos = [new("/managed")] });
        _libraries.Setup(value => value.GetItemById<CollectionFolder>(library.Folder.Id)).Returns(library.Folder);
        var options = new LibraryOptions
        {
            StorageSource = supplyForgedReference ? new() { ProviderId = "forged" } : null,
            PathInfos = [new("/client")],
            EnablePhotos = false
        };
        var settings = new Dictionary<string, string> { ["folder"] = "new-folder" };
        _provider.Setup(value => value.UpdateLibraryAsync(source, settings, TestContext.Current.CancellationToken))
            .Callback(() =>
            {
                Assert.Same(source, options.StorageSource);
                Assert.Equal("/managed", Assert.Single(options.PathInfos).Path);
                library.Serializer.Verify(value => value.SerializeToFile(It.IsAny<object>(), It.IsAny<string>()), Times.Never);
            })
            .Returns(Task.CompletedTask);

        var result = await Controller().UpdateLibraryOptions(new() { Id = library.Folder.Id, LibraryOptions = options, StorageOptions = settings }, TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
        var saved = library.Folder.GetLibraryOptions();
        Assert.Same(source, saved.StorageSource);
        Assert.False(saved.EnablePhotos);
        Assert.Equal("/managed", Assert.Single(saved.PathInfos).Path);
        library.Serializer.Verify(value => value.SerializeToFile(It.IsAny<LibraryOptions>(), It.IsAny<string>()), Times.Once);
        _provider.Verify(value => value.UpdateLibraryAsync(source, settings, TestContext.Current.CancellationToken), Times.Once);
        _libraries.Verify(value => value.CreateShortcut(It.IsAny<string>(), It.IsAny<MediaPathInfo>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_PreservesOptionsWhenProviderIsUnavailableOrValidationFails(bool providerAvailable)
    {
        var source = ConfigureManagedLibrary();
        using var library = new LibraryOptionsScope(new() { StorageSource = source });
        _libraries.Setup(value => value.GetItemById<CollectionFolder>(library.Folder.Id)).Returns(library.Folder);
        var options = new LibraryOptions();
        if (providerAvailable)
        {
            _provider.Setup(value => value.UpdateLibraryAsync(source, It.IsAny<IReadOnlyDictionary<string, string>>(), TestContext.Current.CancellationToken))
                .ThrowsAsync(new ArgumentException("Invalid folder."));
            Assert.IsType<BadRequestObjectResult>(await Controller().UpdateLibraryOptions(new() { Id = library.Folder.Id, LibraryOptions = options }, TestContext.Current.CancellationToken));
        }
        else
        {
            _provider.Setup(value => value.Id).Returns("other");
            Assert.IsType<ConflictObjectResult>(await Controller().UpdateLibraryOptions(new() { Id = library.Folder.Id, LibraryOptions = options }, TestContext.Current.CancellationToken));
        }

        library.Serializer.Verify(value => value.SerializeToFile(It.IsAny<object>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Update_PreservesOrdinaryLibraryBehaviorAndCannotAssignProvider()
    {
        using var library = new LibraryOptionsScope(new());
        _libraries.Setup(value => value.GetItemById<CollectionFolder>(library.Folder.Id)).Returns(library.Folder);
        var options = new LibraryOptions { StorageSource = new() { ProviderId = "forged" } };

        Assert.IsType<NoContentResult>(await Controller().UpdateLibraryOptions(new() { Id = library.Folder.Id, LibraryOptions = options }, TestContext.Current.CancellationToken));

        Assert.Null(library.Folder.GetLibraryOptions().StorageSource);
        _provider.Verify(value => value.UpdateLibraryAsync(It.IsAny<LibraryStorageSource>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_CancellationDoesNotSaveOptions()
    {
        var source = ConfigureManagedLibrary();
        using var library = new LibraryOptionsScope(new() { StorageSource = source });
        _libraries.Setup(value => value.GetItemById<CollectionFolder>(library.Folder.Id)).Returns(library.Folder);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _provider.Setup(value => value.UpdateLibraryAsync(source, It.IsAny<IReadOnlyDictionary<string, string>>(), cancellation.Token))
            .Returns(Task.FromCanceled(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Controller().UpdateLibraryOptions(new() { Id = library.Folder.Id, LibraryOptions = new() }, cancellation.Token));

        library.Serializer.Verify(value => value.SerializeToFile(It.IsAny<object>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Update_RejectsProviderSettingsForOrdinaryLibrary()
    {
        using var library = new LibraryOptionsScope(new());
        _libraries.Setup(value => value.GetItemById<CollectionFolder>(library.Folder.Id)).Returns(library.Folder);

        var result = await Controller().UpdateLibraryOptions(
            new() { Id = library.Folder.Id, LibraryOptions = new(), StorageOptions = new() { ["folder"] = "remote" } },
            TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        library.Serializer.Verify(value => value.SerializeToFile(It.IsAny<object>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void PathChanges_RejectProviderManagedLibraries()
    {
        ConfigureManagedLibrary();
        var controller = Controller();

        Assert.IsType<BadRequestObjectResult>(controller.AddMediaPath(new() { Name = "Movies", Path = "/client" }));
        Assert.IsType<BadRequestObjectResult>(controller.UpdateMediaPath(new() { Name = "Movies", PathInfo = new("/client") }));
        Assert.IsType<BadRequestObjectResult>(controller.RemoveMediaPath("Movies", "/managed"));

        _libraries.Verify(value => value.AddMediaPath(It.IsAny<string>(), It.IsAny<MediaPathInfo>()), Times.Never);
        _libraries.Verify(value => value.UpdateMediaPath(It.IsAny<string>(), It.IsAny<MediaPathInfo>()), Times.Never);
        _libraries.Verify(value => value.RemoveMediaPath(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private LibraryStorageSource ConfigureManagedLibrary()
    {
        var source = new LibraryStorageSource { ProviderId = "example", SourceId = Guid.NewGuid() };
        _provider.Setup(value => value.Id).Returns(source.ProviderId);
        _libraries.Setup(value => value.GetVirtualFolders()).Returns([new() { Name = "Movies", LibraryOptions = new() { StorageSource = source } }]);
        return source;
    }

    private LibraryStructureController Controller()
        => new(
            _configuration.Object,
            _libraries.Object,
            Mock.Of<ILibraryMonitor>(),
            Mock.Of<IDirectoryService>(),
            Mock.Of<IUserManager>(),
            [_provider.Object]);

    private sealed class LibraryOptionsScope : IDisposable
    {
        private readonly IXmlSerializer _previousSerializer = CollectionFolder.XmlSerializer;
        private readonly IServerApplicationHost _previousHost = CollectionFolder.ApplicationHost;

        public LibraryOptionsScope(LibraryOptions options)
        {
            Folder = new CollectionFolder { Id = Guid.NewGuid(), Path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) };
            Serializer.Setup(value => value.DeserializeFromFile(typeof(LibraryOptions), Path.Combine(Folder.Path, "options.xml"))).Returns(options);
            var host = new Mock<IServerApplicationHost>();
            host.Setup(value => value.ExpandVirtualPath(It.IsAny<string>())).Returns((string path) => path);
            host.Setup(value => value.ReverseVirtualPath(It.IsAny<string>())).Returns((string path) => path);
            CollectionFolder.XmlSerializer = Serializer.Object;
            CollectionFolder.ApplicationHost = host.Object;
        }

        public CollectionFolder Folder { get; }

        public Mock<IXmlSerializer> Serializer { get; } = new();

        public void Dispose()
        {
            CollectionFolder.OnCollectionFolderChange();
            CollectionFolder.XmlSerializer = _previousSerializer;
            CollectionFolder.ApplicationHost = _previousHost;
        }
    }
}
