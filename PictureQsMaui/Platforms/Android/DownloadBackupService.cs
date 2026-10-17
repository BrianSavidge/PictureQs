using Android.Content;
using Android.Database;
using Android.Provider;
using PictureQsMaui;
using PictureQsMaui.Services;
using AndroidUri = Android.Net.Uri;

namespace PictureQsMaui.Services;

public sealed partial class DownloadBackupService
{
    public async partial Task<IReadOnlyList<BackupArchiveFile>> GetArchivesAsync()
    {
        var activity = Platform.CurrentActivity as MainActivity
            ?? throw new InvalidOperationException("The backup file picker is unavailable.");
        var archiveUri = await activity.PickBackupFileAsync();
        if (archiveUri is null)
        {
            throw new OperationCanceledException();
        }

        var projection = new[] { DocumentsContract.Document.ColumnDisplayName };
        using var cursor = activity.ContentResolver?.Query(archiveUri, projection, null, null, null)
            ?? throw new IOException("The selected file could not be read.");
        if (!cursor.MoveToFirst())
        {
            throw new IOException("The selected file could not be identified.");
        }

        var name = cursor.GetString(0);
        if (string.IsNullOrWhiteSpace(name) ||
            !name.StartsWith("PictureQs-backup", StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Select a ZIP file whose name starts with 'PictureQs-backup' and ends with '.zip'.");
        }

        return new[]
        {
            new BackupArchiveFile(
                name,
                archiveUri.ToString() ?? throw new IOException("The backup location is invalid."))
        };
    }

    public async partial Task<Stream> OpenArchiveAsync(BackupArchiveFile archive)
    {
        var contentResolver = Platform.CurrentActivity?.ContentResolver
            ?? throw new InvalidOperationException("The Downloads folder is unavailable.");
        var archiveUri = AndroidUri.Parse(archive.Location)
            ?? throw new IOException($"The backup '{archive.Name}' has an invalid location.");
        var stream = contentResolver.OpenInputStream(archiveUri);
        if (stream is null)
        {
            throw new IOException($"The backup '{archive.Name}' could not be opened.");
        }

        return stream;
    }

    public partial Task DeleteArchiveAsync(BackupArchiveFile archive)
    {
        var contentResolver = Platform.CurrentActivity?.ContentResolver
            ?? throw new InvalidOperationException("The Downloads folder is unavailable.");
        var archiveUri = AndroidUri.Parse(archive.Location)
            ?? throw new IOException($"The backup '{archive.Name}' has an invalid location.");
        if (!DocumentsContract.DeleteDocument(contentResolver, archiveUri))
        {
            throw new IOException($"The backup '{archive.Name}' could not be deleted.");
        }

        return Task.CompletedTask;
    }
}
