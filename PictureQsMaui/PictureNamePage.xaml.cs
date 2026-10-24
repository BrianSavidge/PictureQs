namespace PictureQsMaui;

public partial class PictureNamePage : ContentPage
{
    private readonly TaskCompletionSource<PictureImportNames?> _completion = new();
    private PictureImportNames? _result;

    public Task<PictureImportNames?> Completion => _completion.Task;
    public bool DeleteRequested { get; private set; }

    public PictureNamePage(
        string suggestedPictureName,
        string? suggestedTargetsFileName = null,
        string title = "Name picture",
        ImageSource? previewSource = null,
        IEnumerable<string>? existingTargetsFileNames = null,
        bool hasCurrentTargetsFile = false,
        IEnumerable<string>? availableTargetsFileNames = null)
    {
        InitializeComponent();
        Title = title;
        if (string.Equals(title, "Edit picture names", StringComparison.Ordinal))
        {
            TargetsFileNameLabel.Text = "Points of Interest Filename";
        }

        PicturePreview.Source = previewSource;
        PictureNameEntry.Text = suggestedPictureName;
        TargetsFileNameEntry.Text = suggestedTargetsFileName ?? suggestedPictureName;
        _existingTargetsFileNames = new HashSet<string>(
            existingTargetsFileNames ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        _availableTargetsFileNames = (availableTargetsFileNames ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _hasCurrentTargetsFile = hasCurrentTargetsFile;
        DeletePictureButton.IsVisible = hasCurrentTargetsFile;
    }

    private readonly HashSet<string> _existingTargetsFileNames;
    private readonly string[] _availableTargetsFileNames;
    private readonly bool _hasCurrentTargetsFile;

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _completion.TrySetResult(_result);
    }

    private void OnSyncClicked(object? sender, EventArgs e)
    {
        TargetsFileNameEntry.Text = PictureNameEntry.Text;
    }

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        if (_availableTargetsFileNames.Length == 0)
        {
            await DisplayAlertAsync(
                "No Points of Interest",
                "There are no Points of Interest available to choose.",
                "OK");
            return;
        }

        var selectedFileName = await DisplayActionSheetAsync(
            "Choose a point of interest file",
            "Cancel",
            null,
            _availableTargetsFileNames);
        if (!string.IsNullOrEmpty(selectedFileName) &&
            !string.Equals(selectedFileName, "Cancel", StringComparison.Ordinal))
        {
            TargetsFileNameEntry.Text = selectedFileName;
        }
    }

    private async void OnAcceptClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PictureNameEntry.Text) ||
            string.IsNullOrWhiteSpace(TargetsFileNameEntry.Text))
        {
            await DisplayAlertAsync("Names required", "Enter a name for the picture and points of interest files.", "OK");
            return;
        }

        var targetsFileName = TargetsFileNameEntry.Text.Trim();
        var reuseExistingTargetsFile = _existingTargetsFileNames.Contains(targetsFileName);
        if (reuseExistingTargetsFile)
        {
            var useExisting = await DisplayAlertAsync(
                "Point of Interest file already exists",
                _hasCurrentTargetsFile
                    ? $"Use the existing '{targetsFileName}' file and delete this picture's current Point of Interest file if no other picture uses it and it has one?"
                    : $"Use the existing '{targetsFileName}' file for this picture?",
                "Use existing",
                "Change name");
            if (!useExisting)
            {
                return;
            }
        }

        _result = new PictureImportNames(
            PictureNameEntry.Text.Trim(),
            targetsFileName,
            reuseExistingTargetsFile);
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private async void OnDeletePictureClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync(
            "Delete picture",
            $"Delete '{PictureNameEntry.Text}'?",
            "Delete Picture",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        DeleteRequested = true;
        await Navigation.PopModalAsync();
    }
}

public sealed record PictureImportNames(
    string PictureName,
    string TargetsFileName,
    bool ReuseExistingTargetsFile);
