using EquipmentManagement.Infrastructure.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Proves the integration suite's private files go to this run's temporary
/// storage root and that the storage guard/cleanup only ever accepts or deletes
/// a run-owned directory (ADR-075). Nothing here touches the API's development
/// private-storage/; the only directories created and deleted are throwaway
/// run roots under the shared test parent.
/// </summary>
public sealed class TestStorageIsolationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public void Root_IsARunOwnedTempDirectory_OutsideTheRepositoryAndTheDevStorage()
    {
        var root = TestStorage.RootPath;

        Assert.Equal(TestStorage.RootFor(TestRun.Id), root);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), root, StringComparison.OrdinalIgnoreCase);
        Assert.False(IsUnder(root, RepositoryRoot()));
        Assert.NotEqual(Path.GetFullPath(DevStoragePath()), root, StringComparer.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(root, TestStorage.OwnershipMarkerFileName)));
    }

    [Fact]
    public void RootFor_IsUniquePerRunId()
    {
        var roots = Enumerable.Range(0, 1000).Select(_ => TestStorage.RootFor(Guid.NewGuid().ToString("N"))).ToList();

        Assert.Equal(roots.Count, roots.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ApplicationUnderTest_IsConfiguredWithThisRunsRoot()
    {
        var options = factory.Services.GetRequiredService<IOptions<PrivateStorageOptions>>().Value;

        Assert.Equal(TestStorage.RootPath, options.RootPath);
    }

    [Fact]
    public async Task FilesWrittenThroughPrivateStorage_LandUnderThisRunsRoot()
    {
        var storage = factory.Services.GetRequiredService<IPrivateFileStorage>();
        var key = $"isolation-probe/{Guid.NewGuid():N}.bin";

        await storage.SaveAsync(key, new MemoryStream([1, 2, 3]));
        try
        {
            Assert.True(File.Exists(Path.Combine(TestStorage.RootPath, key)));
            Assert.False(File.Exists(Path.Combine(DevStoragePath(), key)));
        }
        finally
        {
            await storage.DeleteAsync(key);
        }
    }

    [Fact]
    public void Guard_AcceptsThisRunsRoot()
    {
        TestStorage.EnsureIsThisRunsRoot(TestStorage.RootPath);
    }

    [Fact]
    public void Guard_RejectsEveryDirectoryThatIsNotThisRunsRoot()
    {
        var api = factory.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;
        var rejected = new[]
        {
            RepositoryRoot(),
            Path.Combine(RepositoryRoot(), "src", "backend"),
            api,
            DevStoragePath(),
            Path.Combine(api, "private-storage-demo"),
            TestStorage.ParentPath,
            Path.Combine(TestStorage.RootPath, "isolation-probe"),
            Path.Combine(TestStorage.ParentPath, "some-sibling"),
            TestStorage.RootFor(Guid.NewGuid().ToString("N")),
            Path.GetTempPath(),
        };

        foreach (var path in rejected)
        {
            Assert.Throws<InvalidOperationException>(() => TestStorage.EnsureIsThisRunsRoot(path));
        }
    }

    [Fact]
    public void Guard_RejectsAnExistingSimilarlyNamedDirectoryOwnedByAnotherRun()
    {
        var otherRunId = Guid.NewGuid().ToString("N");
        var otherRoot = TestStorage.CreateOwnedRunRoot(otherRunId);
        try
        {
            Assert.Throws<InvalidOperationException>(() => TestStorage.EnsureIsThisRunsRoot(otherRoot));
        }
        finally
        {
            TestStorage.DeleteOwnedRunRoot(otherRoot, otherRunId);
        }
    }

    [Fact]
    public void Cleanup_DeletesOnlyTheOwnedRoot_AndRefusesUnownedDirectories()
    {
        var ownedRunId = Guid.NewGuid().ToString("N");
        var siblingRunId = Guid.NewGuid().ToString("N");
        var owned = TestStorage.CreateOwnedRunRoot(ownedRunId);
        var sibling = TestStorage.CreateOwnedRunRoot(siblingRunId);
        var unowned = TestStorage.RootFor(Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unowned); // right place and shape, but no ownership marker
        try
        {
            Directory.CreateDirectory(Path.Combine(owned, "photos"));
            File.WriteAllBytes(Path.Combine(owned, "photos", "probe.jpg"), [1]);

            TestStorage.DeleteOwnedRunRoot(owned, ownedRunId);

            Assert.False(Directory.Exists(owned));
            Assert.True(Directory.Exists(sibling));
            Assert.True(Directory.Exists(TestStorage.ParentPath));
            Assert.True(Directory.Exists(TestStorage.RootPath));

            var unownedRunId = Path.GetFileName(unowned);
            Assert.Throws<InvalidOperationException>(() => TestStorage.DeleteOwnedRunRoot(unowned, unownedRunId));
            Assert.True(Directory.Exists(unowned));
            Assert.Throws<InvalidOperationException>(() => TestStorage.DeleteOwnedRunRoot(sibling, ownedRunId));
            Assert.True(Directory.Exists(sibling));
        }
        finally
        {
            if (Directory.Exists(sibling))
            {
                TestStorage.DeleteOwnedRunRoot(sibling, siblingRunId);
            }

            // Created empty by this test a few lines above; removed non-recursively.
            if (Directory.Exists(unowned))
            {
                Directory.Delete(unowned);
            }
        }
    }

    private string DevStoragePath() =>
        Path.Combine(factory.Services.GetRequiredService<IHostEnvironment>().ContentRootPath, "private-storage");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EquipmentManagement.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static bool IsUnder(string path, string parent) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
