using Android.App;
using Android.Content.PM;
using Android.Content;
using Android.OS;
using AndroidUri = Android.Net.Uri;

namespace PictureQsMaui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int PickBackupFileRequestCode = 7492;
    private TaskCompletionSource<AndroidUri?>? _backupFilePickerCompletion;

    public Task<AndroidUri?> PickBackupFileAsync()
    {
        if (_backupFilePickerCompletion is not null)
        {
            throw new InvalidOperationException("The backup file picker is already open.");
        }

        _backupFilePickerCompletion = new TaskCompletionSource<AndroidUri?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("application/zip");
            intent.AddFlags(
                ActivityFlags.GrantReadUriPermission |
                ActivityFlags.GrantWriteUriPermission |
                ActivityFlags.GrantPersistableUriPermission);
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                intent.PutExtra(
                    Android.Provider.DocumentsContract.ExtraInitialUri,
                    AndroidUri.Parse("content://com.android.externalstorage.documents/document/primary%3ADownload"));
            }

            StartActivityForResult(intent, PickBackupFileRequestCode);
        }
        catch
        {
            _backupFilePickerCompletion = null;
            throw;
        }

        return _backupFilePickerCompletion.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickBackupFileRequestCode || _backupFilePickerCompletion is not { } completion)
        {
            return;
        }

        _backupFilePickerCompletion = null;
        var selectedUri = resultCode == Result.Ok ? data?.Data : null;
        if (selectedUri is not null && data is not null)
        {
            var permissionFlags = data.Flags &
                (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            ContentResolver?.TakePersistableUriPermission(selectedUri, permissionFlags);
        }

        completion.TrySetResult(selectedUri);
    }
}
