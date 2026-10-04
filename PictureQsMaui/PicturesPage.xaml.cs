using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PictureQsMaui.Services;

namespace PictureQsMaui;

public partial class PicturesPage : ContentPage
{
    private readonly PictureLibraryService _pictureLibrary;
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
                string.Equals(pictureFileName, selectedPictureFileName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var picture in pictureItems)
        {
            picture.SetPictureItems(pictureItems);
            Pictures.Add(picture);
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
            foreach (var photo in photos)
            {
                lastImportedPictureFileName = await _pictureLibrary.ImportPictureAsync(photo);
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

    private async void OnDeletePictureClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: PictureListItem picture })
        {
            return;
        }

        if (Pictures.Count <= 1)
        {
            await DisplayAlertAsync("Cannot delete picture", "At least one picture must remain in the library.", "OK");
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Delete picture",
            $"Delete '{picture.PictureFileName}'?",
            "Delete",
            "Cancel");
        if (!confirmed)
        {
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

        public PictureListItem(string pictureFileName, string targetsFileName, bool isSelected)
        {
            PictureFileName = pictureFileName;
            _targetsFileName = targetsFileName;
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
