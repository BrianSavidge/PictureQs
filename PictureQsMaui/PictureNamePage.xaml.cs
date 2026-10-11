namespace PictureQsMaui;

public partial class PictureNamePage : ContentPage
{
    private readonly TaskCompletionSource<PictureImportNames?> _completion = new();
    private PictureImportNames? _result;

    public Task<PictureImportNames?> Completion => _completion.Task;

    public PictureNamePage(
        string suggestedPictureName,
        string? suggestedTargetsFileName = null,
        string title = "Name picture",
        ImageSource? previewSource = null,
        IEnumerable<string>? existingTargetsFileNames = null,
        bool hasCurrentTargetsFile = false)
    {
        InitializeComponent();
        Title = title;
        PicturePreview.Source = previewSource;
        PictureNameEntry.Text = suggestedPictureName;
        TargetsFileNameEntry.Text = suggestedTargetsFileName ?? suggestedPictureName;
        _existingTargetsFileNames = new HashSet<string>(
            existingTargetsFileNames ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        _hasCurrentTargetsFile = hasCurrentTargetsFile;
    }

    private readonly HashSet<string> _existingTargetsFileNames;
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

    private async void OnAcceptClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PictureNameEntry.Text) ||
            string.IsNullOrWhiteSpace(TargetsFileNameEntry.Text))
        {
            await DisplayAlertAsync("Names required", "Enter a name for the picture and JSON file.", "OK");
            return;
        }

        var targetsFileName = TargetsFileNameEntry.Text.Trim();
        var reuseExistingTargetsFile = _existingTargetsFileNames.Contains(targetsFileName);
        if (reuseExistingTargetsFile)
        {
            var useExisting = await DisplayAlertAsync(
                "JSON file already exists",
                _hasCurrentTargetsFile
                    ? $"Use the existing '{targetsFileName}.json' file and delete this picture's current JSON file if no other picture uses it?"
                    : $"Use the existing '{targetsFileName}.json' file for this picture?",
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
}

public sealed record PictureImportNames(
    string PictureName,
    string TargetsFileName,
    bool ReuseExistingTargetsFile);
