namespace RestOMatic.Web.Infrastructure.Storage;

/// <summary>
/// The one directory that holds everything the app must not lose. Nothing
/// persistent is written outside it.
/// </summary>
public sealed class DataDirectory(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public string DatabaseFile => System.IO.Path.Combine(Path, "rest-o-matic-ui.db");

    public string KeysDirectory => System.IO.Path.Combine(Path, "keys");

    /// <summary>
    /// Creates the directory if it is missing and proves it can be written
    /// to, so that a bad location fails with one message that names it and
    /// not with a database error later.
    /// </summary>
    public void EnsureWritable()
    {
        try
        {
            Directory.CreateDirectory(Path);
            var probe = System.IO.Path.Combine(Path, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The data directory '{Path}' cannot be created or written to. " +
                "Set DataDirectory to a location the app can write to.", ex);
        }
    }
}
