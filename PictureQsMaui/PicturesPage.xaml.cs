using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Mail;
using System.Runtime.CompilerServices;
using System.Text.Json;
using PictureQsMaui.Services;

namespace PictureQsMaui;

public partial class PicturesPage : ContentPage
{
    private readonly PictureLibraryService _pictureLibrary;
    private readonly DownloadBackupService _downloadBackups = new();
    private string _activePictureFileName;

    public ObservableCollection<PictureListItem> Pictures { get; } = new();
    public bool Confirmed { get; private set; }
    public string? SelectedPictureFileName { get; private set; }

    public PicturesPage(PictureLibraryService pictureLibrary, string selectedPictureFileName)
    {
        _pictureLibrary = pictureLibrary;
        _activePictureFileName = selectedPictureFileName;
        InitializeComponent();
        BindingContext = this;
        LoadPictures(selectedPictureFileName);
    }

    private void LoadPictures(string selectedPictureFileName)
    {
        Pictures.Clear();
        var pictureItems = _pictureLibrary.GetPictureFileNames()
            .Select(pictureFileName => new PictureListItem(
                pictureFileName,
                _pictureLibrary.GetTargetsFileName(pictureFileName),
                ImageSource.FromFile(_pictureLibrary.GetPicturePath(pictureFileName)),
                string.Equals(pictureFileName, selectedPictureFileName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var picture in pictureItems)
        {
            picture.SetPictureItems(pictureItems);
            Pictures.Add(picture);
        }
    }

    private void OnFooterSizeChanged(object? sender, EventArgs e)
    {
        const double buttonSpacing = 8;
        var addPictureWidth = MeasureButtonWidth(AddPictureButton, "Add picture");
        var backupWidth = MeasureButtonWidth(BackupButton, "Backup");
        var restoreWidth = MeasureButtonWidth(RestoreButton, "Restore");
        var pictureActionsWidth = addPictureWidth + backupWidth + restoreWidth + buttonSpacing * 2;
        var dialogActionsWidth = DialogActions.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        var groupsWouldOverlap = pictureActionsWidth + dialogActionsWidth + FooterLayout.ColumnSpacing > FooterLayout.Width;

        AddPictureButton.Text = groupsWouldOverlap ? "Add" : "Add picture";
        BackupButton.Text = groupsWouldOverlap ? "Bak" : "Backup";
        RestoreButton.Text = groupsWouldOverlap ? "Res" : "Restore";
    }

    private static double MeasureButtonWidth(Button button, string text)
    {
        var originalText = button.Text;
        button.Text = text;
        var width = button.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        button.Text = originalText;
        return width;
    }

    private async void OnBackupClicked(object? sender, EventArgs e)
    {
        var enteredEmail = await DisplayPromptAsync(
            "Back up pictures",
            "Enter the email address to send the backup to.",
            "Continue",
            "Cancel",
            keyboard: Keyboard.Email);
        if (string.IsNullOrWhiteSpace(enteredEmail))
        {
            return;
        }

        var emailAddress = enteredEmail.Trim();
        if (!MailAddress.TryCreate(emailAddress, out var parsedAddress) ||
            !string.Equals(parsedAddress.Address, emailAddress, StringComparison.OrdinalIgnoreCase))
        {
            await DisplayAlertAsync("Invalid email address", "Enter a valid email address and try again.", "OK");
            return;
        }

        string archivePath;
        try
        {
            archivePath = await _pictureLibrary.CreateBackupArchiveAsync();
        }
        catch (IOException ex)
        {
            await DisplayAlertAsync("Could not create backup", ex.Message, "OK");
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            await DisplayAlertAsync("Could not create backup", ex.Message, "OK");
            return;
        }
        catch (InvalidOperationException ex)
        {
            await DisplayAlertAsync("Could not create backup", ex.Message, "OK");
            return;
        }

        try
        {
            var message = new EmailMessage
            {
                Subject = "PictureQs backup",
                Body = "Attached is a backup of the pictures and JSON files stored by PictureQs.",
                To = new List<string> { parsedAddress.Address },
                Attachments = new List<EmailAttachment> { new(archivePath) }
            };
            await Email.Default.ComposeAsync(message);
        }
        catch (FeatureNotSupportedException ex)
        {
            await ShareBackupAsync(archivePath, emailAddress, ex.Message);
        }
        catch (FeatureNotEnabledException ex)
        {
            await ShareBackupAsync(archivePath, emailAddress, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            await ShareBackupAsync(archivePath, emailAddress, ex.Message);
        }
        catch (IOException ex)
        {
            await DisplayAlertAsync("Could not open email", ex.Message, "OK");
        }
        catch (InvalidOperationException ex)
        {
            await DisplayAlertAsync("Could not open email", ex.Message, "OK");
        }
    }

    private async void OnRestoreClicked(object? sender, EventArgs e)
    {
        try
        {
            var backups = await _downloadBackups.GetArchivesAsync();
            if (backups.Count == 0)
            {
                await DisplayAlertAsync(
                    "No backups found",
                    "There are no ZIP files whose names start with 'PictureQs-backup' in the Downloads folder.",
                    "OK");
                return;
            }

            BackupArchiveFile? selectedBackup;
            if (backups.Count == 1)
            {
                selectedBackup = backups[0];
            }
            else
            {
                var archiveNames = backups.Select(backup => backup.Name).ToArray();
                var selectedName = await DisplayActionSheetAsync(
                    "Choose a backup from Downloads",
                    "Cancel",
                    null,
                    archiveNames);
                selectedBackup = backups.FirstOrDefault(
                    backup => string.Equals(backup.Name, selectedName, StringComparison.Ordinal));
            }

            if (selectedBackup is null)
            {
                return;
            }

            var confirmed = await DisplayAlertAsync(
                "Restore backup",
                $"Restore '{selectedBackup.Name}'? Matching pictures and JSON files in this app will be overwritten.",
                "Restore",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            await using (var backupStream = await _downloadBackups.OpenArchiveAsync(selectedBackup))
            {
                await _pictureLibrary.RestoreBackupArchiveAsync(backupStream);
            }

            _activePictureFileName = _pictureLibrary.SelectedPictureFileName;
            Confirmed = true;
            SelectedPictureFileName = _activePictureFileName;
            LoadPictures(_activePictureFileName);

            var deleteBackup = await DisplayAlertAsync(
                "Delete downloaded backup?",
                $"The restore is complete. Delete '{selectedBackup.Name}' from Downloads?",
                "Delete",
                "Keep");
            if (deleteBackup)
            {
                try
                {
                    await _downloadBackups.DeleteArchiveAsync(selectedBackup);
                }
                catch (IOException ex)
                {
                    await DisplayAlertAsync("Could not delete backup", ex.Message, "OK");
                }
                catch (UnauthorizedAccessException ex)
                {
                    await DisplayAlertAsync("Could not delete backup", ex.Message, "OK");
                }
                catch (InvalidOperationException ex)
                {
                    await DisplayAlertAsync("Could not delete backup", ex.Message, "OK");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidDataException ex)
        {
            await DisplayAlertAsync("Invalid backup", ex.Message, "OK");
        }
        catch (JsonException ex)
        {
            await DisplayAlertAsync("Invalid backup", ex.Message, "OK");
        }
        catch (IOException ex)
        {
            await DisplayAlertAsync("Could not restore backup", ex.Message, "OK");
        }
        catch (UnauthorizedAccessException ex)
        {
            await DisplayAlertAsync("Could not restore backup", ex.Message, "OK");
        }
        catch (InvalidOperationException ex)
        {
            await DisplayAlertAsync("Could not restore backup", ex.Message, "OK");
        }
        catch (FeatureNotSupportedException ex)
        {
            await DisplayAlertAsync("Restore unavailable", ex.Message, "OK");
        }
    }

    private async Task ShareBackupAsync(string archivePath, string emailAddress, string emailError)
    {
        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Share PictureQs backup",
                File = new ShareFile(archivePath)
            });
            await DisplayAlertAsync(
                "Choose an email app",
                $"The email composer is unavailable ({emailError}). Choose an email app in the sharing menu and enter {emailAddress} as the recipient.",
                "OK");
        }
        catch (FeatureNotSupportedException ex)
        {
            await DisplayAlertAsync(
                "Backup sharing unavailable",
                $"Email is unavailable ({emailError}), and sharing is not supported on this device: {ex.Message}",
                "OK");
        }
        catch (NotSupportedException ex)
        {
            await DisplayAlertAsync(
                "Backup sharing unavailable",
                $"Email is unavailable ({emailError}), and sharing is not supported on this device: {ex.Message}",
                "OK");
        }
        catch (InvalidOperationException ex)
        {
            await DisplayAlertAsync(
                "Backup sharing unavailable",
                $"Email is unavailable ({emailError}), and the backup could not be shared: {ex.Message}",
                "OK");
        }
    }

    private async void OnAddPictureClicked(object? sender, EventArgs e)
    {
        try
        {
            var photos = await MediaPicker.Default.PickPhotosAsync();
            if (photos is null || photos.Count == 0)
            {
                return;
            }

            string? lastImportedPictureFileName = null;
            var existingTargetFileNames = Pictures
                .Select(picture => picture.TargetsFileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var photo in photos)
            {
                var namePage = new PictureNamePage(
                    Path.GetFileNameWithoutExtension(photo.FileName),
                    previewSource: ImageSource.FromFile(photo.FullPath),
                    existingTargetsFileNames: existingTargetFileNames,
                    availableTargetsFileNames: _pictureLibrary.GetTargetsJsonFileNames());
                await Navigation.PushModalAsync(new NavigationPage(namePage));
                var names = await namePage.Completion;
                if (names is null)
                {
                    break;
                }

                try
                {
                    lastImportedPictureFileName = await _pictureLibrary.ImportPictureAsync(
                        photo,
                        names.PictureName,
                        names.TargetsFileName);
                    existingTargetFileNames.Add(names.TargetsFileName);
                }
                catch (InvalidDataException ex)
                {
                    await DisplayAlertAsync("Invalid name", ex.Message, "OK");
                }
                catch (IOException ex)
                {
                    await DisplayAlertAsync("Could not add picture", ex.Message, "OK");
                }
            }

            if (lastImportedPictureFileName is not null)
            {
                LoadPictures(lastImportedPictureFileName);
            }
        }
        catch (FeatureNotSupportedException ex)
        {
            await DisplayAlertAsync("Gallery unavailable", ex.Message, "OK");
        }
        catch (PermissionException ex)
        {
            await DisplayAlertAsync("Permission denied", ex.Message, "OK");
        }
        catch (IOException ex)
        {
            await DisplayAlertAsync("Could not add picture", ex.Message, "OK");
        }
        catch (InvalidDataException ex)
        {
            await DisplayAlertAsync("Unsupported picture", ex.Message, "OK");
        }
    }

    private async void OnEditPictureNamesClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: PictureListItem picture })
        {
            return;
        }

        var namePage = new PictureNamePage(
            picture.PictureName,
            picture.TargetsFileName,
            "Edit picture names",
            ImageSource.FromFile(_pictureLibrary.GetPicturePath(picture.PictureFileName)),
            Pictures
                .Where(item => !ReferenceEquals(item, picture))
                .Select(item => item.TargetsFileName),
            hasCurrentTargetsFile: true,
            availableTargetsFileNames: _pictureLibrary.GetTargetsJsonFileNames());
        await Navigation.PushModalAsync(new NavigationPage(namePage));
        var names = await namePage.Completion;
        if (namePage.DeleteRequested)
        {
            await DeletePictureAsync(picture);
            return;
        }

        if (names is null)
        {
            return;
        }

        var targetFileNames = Pictures.ToDictionary(
            item => item.PictureFileName,
            item => ReferenceEquals(item, picture) ? names.TargetsFileName : item.TargetsFileName.Trim(),
            StringComparer.OrdinalIgnoreCase);
        var selectedPicture = Pictures.FirstOrDefault(item => item.IsSelected);
        var selectedPictureFileName = selectedPicture?.PictureFileName ?? _activePictureFileName;
        var renamedSelectedPictureFileName = string.Equals(
            selectedPictureFileName,
            picture.PictureFileName,
            StringComparison.OrdinalIgnoreCase)
                ? $"{names.PictureName}{Path.GetExtension(picture.PictureFileName)}"
                : selectedPictureFileName;
        try
        {
            var renamedPictureFileName = await _pictureLibrary.RenamePictureAsync(
                picture.PictureFileName,
                names.PictureName,
                names.TargetsFileName,
                renamedSelectedPictureFileName,
                targetFileNames,
                names.ReuseExistingTargetsFile);

            if (string.Equals(
                    picture.PictureFileName,
                    _activePictureFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                _activePictureFileName = renamedPictureFileName;
            }

            Confirmed = true;
            SelectedPictureFileName = renamedSelectedPictureFileName;
            LoadPictures(renamedSelectedPictureFileName);
        }
        catch (InvalidDataException ex)
        {
            await DisplayAlertAsync("Invalid name", ex.Message, "OK");
        }
        catch (IOException ex)
        {
            await DisplayAlertAsync("Could not rename picture", ex.Message, "OK");
        }
        catch (InvalidOperationException ex)
        {
            await DisplayAlertAsync("Could not rename picture", ex.Message, "OK");
        }
    }

    private async Task DeletePictureAsync(PictureListItem picture)
    {
        if (Pictures.Count <= 1)
        {
            await DisplayAlertAsync("Cannot delete picture", "At least one picture must remain in the library.", "OK");
            return;
        }

        var remainingPictures = Pictures.Where(item => !ReferenceEquals(item, picture)).ToList();
        var selectedPicture = remainingPictures.FirstOrDefault(item => item.IsSelected)
            ?? remainingPictures.FirstOrDefault(item => string.Equals(
                item.PictureFileName,
                _activePictureFileName,
                StringComparison.OrdinalIgnoreCase))
            ?? remainingPictures[0];
        var targetFileNames = remainingPictures.ToDictionary(
            item => item.PictureFileName,
            item => string.IsNullOrWhiteSpace(item.TargetsFileName)
                ? _pictureLibrary.GetTargetsFileName(item.PictureFileName)
                : item.TargetsFileName.Trim(),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            await _pictureLibrary.DeletePictureAsync(
                picture.PictureFileName,
                selectedPicture.PictureFileName,
                targetFileNames);

            if (string.Equals(
                    picture.PictureFileName,
                    _activePictureFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                _activePictureFileName = selectedPicture.PictureFileName;
                Confirmed = true;
                SelectedPictureFileName = _activePictureFileName;
            }

            LoadPictures(selectedPicture.PictureFileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            if (!File.Exists(_pictureLibrary.GetPicturePath(picture.PictureFileName)))
            {
                if (string.Equals(
                        picture.PictureFileName,
                        _activePictureFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _activePictureFileName = selectedPicture.PictureFileName;
                    Confirmed = true;
                    SelectedPictureFileName = _activePictureFileName;
                }

                LoadPictures(selectedPicture.PictureFileName);
            }

            await DisplayAlertAsync("Could not delete picture", ex.Message, "OK");
        }
    }

    private async void OnOkClicked(object? sender, EventArgs e)
    {
        var selectedPicture = Pictures.FirstOrDefault(picture => picture.IsSelected);
        if (selectedPicture is null)
        {
            await DisplayAlertAsync("Select a picture", "Choose a picture before continuing.", "OK");
            return;
        }

        var targetFileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var picture in Pictures)
        {
            if (string.IsNullOrWhiteSpace(picture.TargetsFileName))
            {
                await DisplayAlertAsync("Enter a targets file name", $"Enter a file name for '{picture.PictureFileName}'.", "OK");
                return;
            }

            targetFileNames[picture.PictureFileName] = picture.TargetsFileName.Trim();
        }

        try
        {
            await _pictureLibrary.SaveSettingsAsync(selectedPicture.PictureFileName, targetFileNames);
        }
        catch (InvalidDataException ex)
        {
            await DisplayAlertAsync("Invalid file name", ex.Message, "OK");
            return;
        }

        SelectedPictureFileName = selectedPicture.PictureFileName;
        Confirmed = true;
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    public sealed class PictureListItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _targetsFileName;

        public string PictureFileName { get; }
        public string PictureName => Path.GetFileNameWithoutExtension(PictureFileName);
        public ImageSource ThumbnailSource { get; }
        public string TargetsFileName
        {
            get => _targetsFileName;
            set
            {
                if (_targetsFileName == value)
                {
                    return;
                }

                _targetsFileName = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
                if (value)
                {
                    foreach (var picture in _ownerPictures)
                    {
                        if (!ReferenceEquals(picture, this))
                        {
                            picture.IsSelected = false;
                        }
                    }
                }
            }
        }

        private IReadOnlyList<PictureListItem> _ownerPictures = Array.Empty<PictureListItem>();

        public event PropertyChangedEventHandler? PropertyChanged;

        public PictureListItem(
            string pictureFileName,
            string targetsFileName,
            ImageSource thumbnailSource,
            bool isSelected)
        {
            PictureFileName = pictureFileName;
            _targetsFileName = targetsFileName;
            ThumbnailSource = thumbnailSource;
            _isSelected = isSelected;
        }

        internal void SetPictureItems(IReadOnlyList<PictureListItem> pictures)
        {
            _ownerPictures = pictures;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
