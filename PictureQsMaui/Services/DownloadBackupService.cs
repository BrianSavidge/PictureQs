namespace PictureQsMaui.Services;

public sealed partial class DownloadBackupService
{
    public partial Task<IReadOnlyList<BackupArchiveFile>> GetArchivesAsync();

    public partial Task<Stream> OpenArchiveAsync(BackupArchiveFile archive);

    public partial Task DeleteArchiveAsync(BackupArchiveFile archive);
}

public sealed record BackupArchiveFile(string Name, string Location);
