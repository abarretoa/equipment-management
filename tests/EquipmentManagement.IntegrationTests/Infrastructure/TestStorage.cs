using System.Text.RegularExpressions;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// One private-file storage root per integration-test process (ADR-075), the
/// file-system counterpart of <see cref="TestDatabase"/>. Every
/// <see cref="ApiWebApplicationFactory"/> sets <c>PrivateStorage:RootPath</c> —
/// the same key production reads — to
/// <c>&lt;OS temp&gt;/equipment-management-tests/&lt;TestRun.Id&gt;</c>, so the
/// real <c>LocalPrivateFileStorage</c> writes every test photo and PDF there
/// instead of the API's development <c>private-storage/</c>. The root carries an
/// ownership marker; <see cref="IntegrationTestFramework"/> deletes exactly that
/// directory after the run.
/// </summary>
public static partial class TestStorage
{
    public const string ParentDirectoryName = "equipment-management-tests";

    /// <summary>Marker file in the root; its content is the owning run's id.</summary>
    public const string OwnershipMarkerFileName = ".equipment-management-test-run";

    private static readonly Lazy<string> Provisioned = new(Provision, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly object DeleteLock = new();
    private static bool deleted;

    /// <summary>Shared parent under the OS temp directory. Never deleted.</summary>
    public static string ParentPath { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), ParentDirectoryName));

    /// <summary>This run's root path, created (with its marker) on first access.</summary>
    public static string RootPath => Provisioned.Value;

    /// <summary>Loggable form of the root — no user-specific temp prefix.</summary>
    public static string DisplayName => $"%TEMP%/{ParentDirectoryName}/{TestRun.Id}";

    public static bool IsCreated => Provisioned.IsValueCreated;

    /// <summary>The root a given run id owns: always a direct child of <see cref="ParentPath"/>.</summary>
    public static string RootFor(string runId) => Path.Combine(ParentPath, runId);

    /// <summary>Fail-closed check before tests use or delete storage: <paramref name="path"/> must be exactly this run's root.</summary>
    public static void EnsureIsThisRunsRoot(string path) => EnsureOwnedRunRoot(path, TestRun.Id);

    /// <summary>
    /// <paramref name="path"/> must be, exactly, the root of <paramref name="runId"/>:
    /// a well-formed run id, the same full path as <see cref="RootFor"/>, an
    /// existing real directory (not a link), and an ownership marker holding that
    /// run id. Containing "test" or sitting in the temp directory is not enough.
    /// </summary>
    public static void EnsureOwnedRunRoot(string path, string runId)
    {
        var fullPath = Path.GetFullPath(path);

        if (!RunIdPattern().IsMatch(runId) ||
            !string.Equals(fullPath, RootFor(runId), PathComparison) ||
            !Directory.Exists(fullPath) ||
            new DirectoryInfo(fullPath).Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            !string.Equals(ReadMarker(fullPath), runId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to use or delete '{fullPath}' as integration-test storage: " +
                $"only the run-owned directory {ParentDirectoryName}/{runId} (with its ownership marker) is allowed.");
        }
    }

    /// <summary>Deletes this run's root (only ever <see cref="RootPath"/>). Idempotent; a no-op if it was never created.</summary>
    public static void DeleteIfCreated()
    {
        lock (DeleteLock)
        {
            if (deleted || !Provisioned.IsValueCreated)
            {
                return;
            }

            deleted = true;
            DeleteOwnedRunRoot(Provisioned.Value, TestRun.Id);
            IntegrationTestFramework.Log($"Test storage deleted: {DisplayName}");
        }
    }

    /// <summary>
    /// Recursively deletes <paramref name="path"/> after <see cref="EnsureOwnedRunRoot"/>
    /// passes, then confirms it is gone. Never touches the parent or any sibling.
    /// </summary>
    public static void DeleteOwnedRunRoot(string path, string runId)
    {
        EnsureOwnedRunRoot(path, runId);
        var fullPath = Path.GetFullPath(path);

        // A just-closed file handle (or an antivirus scan) can briefly hold a
        // file on Windows; retry the same exact directory, never a broader one.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(fullPath, recursive: true);
                break;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(200 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(200 * attempt);
            }
        }

        if (Directory.Exists(fullPath))
        {
            throw new IOException($"Integration-test storage {ParentDirectoryName}/{runId} still exists after deletion.");
        }
    }

    /// <summary>Creates a new run-owned root with its marker. Refuses to adopt a directory that already exists.</summary>
    public static string CreateOwnedRunRoot(string runId)
    {
        if (!RunIdPattern().IsMatch(runId))
        {
            throw new InvalidOperationException("Malformed test run id.");
        }

        var root = RootFor(runId);
        if (Directory.Exists(root) || File.Exists(root))
        {
            throw new InvalidOperationException($"Integration-test storage {ParentDirectoryName}/{runId} already exists; refusing to reuse it.");
        }

        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, OwnershipMarkerFileName), runId);
        EnsureOwnedRunRoot(root, runId);
        return root;
    }

    private static string Provision()
    {
        var root = CreateOwnedRunRoot(TestRun.Id);
        IntegrationTestFramework.Log($"Test storage created: {DisplayName}");

        // Best effort only, as for TestDatabase; the real teardown is
        // IntegrationTestFramework's assembly runner. DeleteIfCreated is idempotent.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                DeleteIfCreated();
            }
            catch (Exception)
            {
                // Process is exiting; the leftover is documented (README "Running tests").
            }
        };

        return root;
    }

    private static string? ReadMarker(string root)
    {
        var marker = Path.Combine(root, OwnershipMarkerFileName);
        return File.Exists(marker) ? File.ReadAllText(marker) : null;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdPattern();
}
