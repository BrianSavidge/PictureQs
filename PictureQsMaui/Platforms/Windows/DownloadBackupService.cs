namespace PictureQsMaui.Services;

public sealed partial class DownloadBackupService
{
    private static readonly string DownloadsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");

    public partial Task<IReadOnlyList<BackupArchiveFile>> GetArchivesAsync()
    {
        if (!Directory.Exists(DownloadsPath))
        {
            return Task.FromResult<IReadOnlyList<BackupArchiveFile>>(Array.Empty<BackupArchiveFile>());
        }

        IReadOnlyList<BackupArchiveFile> archives = Directory.EnumerateFiles(DownloadsPath)
            .Where(path =>
                Path.GetFileName(path).StartsWith("PictureQs-backup", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Select(path => new BackupArchiveFile(Path.GetFileName(path), path))
            .OrderByDescending(archive => File.GetLastWriteTime(archive.Location))
            .ToList();

        return Task.FromResult(archives);
    }

    public partial Task<Stream> OpenArchiveAsync(BackupArchiveFile archive)
    {
        return Task.FromResult<Stream>(File.OpenRead(archive.Location));
    }

    public partial Task DeleteArchiveAsync(BackupArchiveFile archive)
    {
        File.Delete(archive.Location);
        return Task.CompletedTask;
    }
}
