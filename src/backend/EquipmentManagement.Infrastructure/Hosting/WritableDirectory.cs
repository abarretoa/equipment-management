namespace EquipmentManagement.Infrastructure.Hosting;

/// <summary>
/// Startup check for a directory the application must own: an explicit,
/// absolute path that exists (or can be created) and accepts a real write.
/// Returns a description of the problem, or null when the directory is usable.
/// The description names the setting only, never the configured value.
/// </summary>
public static class WritableDirectory
{
    public static string? Check(string? path, string settingName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return $"{settingName} is not set.";
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return $"{settingName} must be an absolute path.";
        }

        try
        {
            Directory.CreateDirectory(path);

            var probePath = Path.Combine(path, $".write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(probePath, [0x01]);
            File.Delete(probePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            return $"{settingName} is not writable by the application user ({ex.GetType().Name}).";
        }

        return null;
    }
}
