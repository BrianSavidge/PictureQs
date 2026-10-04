using System.Buffers.Binary;
using System.Text.Json;
using System.Windows.Input;
using PictureQsMaui.Models;
using PictureQsMaui.Services;

namespace PictureQsMaui;

public partial class MainPage : ContentPage
{
    private readonly List<PointOfInterest> _pointsOfInterest = new();
    private readonly PictureLibraryService _pictureLibrary = new();
    private readonly ArrowDrawable _arrowDrawable = new();
    private string _activePictureFileName = "picture.png";
    private PointOfInterest? _randomPoi;
    private PointOfInterest? _selectedPoi;
    private bool _isRandomMode;
    private bool _isUpdatingPoiList;
    private int _imagePixelWidth;
    private int _imagePixelHeight;

    public ICommand DeletePoiCommand { get; }

    public MainPage()
    {
        DeletePoiCommand = new Command<PointOfInterest>(async poi => await DeletePoiAsync(poi));
        InitializeComponent();
        MarkerOverlay.Drawable = _arrowDrawable;

        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerPressed += OnMarkerOverlayPressed;
        MarkerOverlay.GestureRecognizers.Add(pointerGesture);

        Loaded += OnPageLoaded;
        LoadPointsOfInterest();
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnPageLoaded;
        await _pictureLibrary.InitializeAsync();
        _activePictureFileName = _pictureLibrary.SelectedPictureFileName;
        await LoadActivePictureAsync();
        LoadPointsOfInterest();
    }

    private async Task LoadActivePictureAsync()
    {
        var imagePath = _pictureLibrary.GetPicturePath(_activePictureFileName);

        var header = new byte[24];
        await using (var stream = File.OpenRead(imagePath))
        {
            await stream.ReadExactlyAsync(header);
        }

        if (!header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            using var imageStream = File.OpenRead(imagePath);
            var image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(imageStream);
            if (image is null)
            {
                throw new InvalidDataException($"The picture at '{imagePath}' could not be loaded.");
            }

            _imagePixelWidth = (int)Math.Round(image.Width);
            _imagePixelHeight = (int)Math.Round(image.Height);
            PictureImage.Source = ImageSource.FromFile(imagePath);
            return;
        }

        _imagePixelWidth = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        _imagePixelHeight = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        if (_imagePixelWidth <= 0 || _imagePixelHeight <= 0)
        {
            throw new InvalidDataException($"The picture at '{imagePath}' has invalid dimensions.");
        }

        PictureImage.Source = ImageSource.FromFile(imagePath);
        UpdateSelectedPoiMarker();
    }

    private void LoadPointsOfInterest()
    {
        _pointsOfInterest.Clear();
        _randomPoi = null;
        var poiFilePath = _pictureLibrary.GetTargetsPath(_activePictureFileName);
        if (File.Exists(poiFilePath))
        {
            var json = File.ReadAllText(poiFilePath);
            var points = JsonSerializer.Deserialize<List<PointOfInterest>>(json);
            if (points is not null)
            {
                _pointsOfInterest.AddRange(points);
            }
        }

        _selectedPoi = null;
        _arrowDrawable.SelectedPoi = null;
        _arrowDrawable.GuessPosition = null;
        MarkerOverlay.Invalidate();
        UpdatePoiList();
    }

    private async void OnPicturesClicked(object? sender, EventArgs e)
    {
        var page = new PicturesPage(_pictureLibrary, _activePictureFileName);
        page.Disappearing += OnPicturesPageDisappearing;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnPicturesPageDisappearing(object? sender, EventArgs e)
    {
        if (sender is not PicturesPage page)
        {
            return;
        }

        page.Disappearing -= OnPicturesPageDisappearing;
        if (!page.Confirmed || page.SelectedPictureFileName is null)
        {
            return;
        }

        _activePictureFileName = page.SelectedPictureFileName;
        await LoadActivePictureAsync();
        LoadPointsOfInterest();
    }

    private void UpdatePoiList()
    {
        var sorted = _pointsOfInterest
            .OrderBy(p => p.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_isRandomMode)
        {
            if (_randomPoi is null || !sorted.Contains(_randomPoi))
            {
                _randomPoi = SelectRandomPoi(sorted, null);
            }
        }

        var selectedPoi = _isRandomMode ? null : _selectedPoi;
        RandomButton.IsEnabled = sorted.Count > 1;
        _isUpdatingPoiList = true;
        try
        {
            PoiListView.ItemsSource = _isRandomMode
                ? _randomPoi is null ? Array.Empty<PointOfInterest>() : new[] { _randomPoi }
                : sorted;
            PoiListView.SelectedItem = selectedPoi;
        }
        finally
        {
            _isUpdatingPoiList = false;
        }

        if (selectedPoi is null)
        {
            _selectedPoi = null;
            _arrowDrawable.SelectedPoi = null;
            MarkerOverlay.Invalidate();
        }
        else
        {
            SelectPoi(selectedPoi);
        }
    }

    private void OnViewClicked(object? sender, EventArgs e)
    {
        _isRandomMode = !_isRandomMode;
        _arrowDrawable.GuessPosition = null;
        ViewModeButton.Text = _isRandomMode ? "Edit Mode" : "Quiz Mode";
        RandomButton.IsVisible = _isRandomMode;
        if (_isRandomMode)
        {
            _randomPoi = SelectRandomPoi(
                _pointsOfInterest.OrderBy(poi => poi.Text, StringComparer.OrdinalIgnoreCase).ToList(),
                null);
        }

        UpdatePoiList();
    }

    private void OnRandomClicked(object? sender, EventArgs e)
    {
        var sorted = _pointsOfInterest
            .OrderBy(poi => poi.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _randomPoi = SelectRandomPoi(sorted, _randomPoi);
        _arrowDrawable.GuessPosition = null;
        UpdatePoiList();
    }

    private static PointOfInterest? SelectRandomPoi(
        IReadOnlyList<PointOfInterest> pointsOfInterest,
        PointOfInterest? currentPoi)
    {
        if (pointsOfInterest.Count == 0)
        {
            return null;
        }

        var currentIndex = currentPoi is null
            ? -1
            : pointsOfInterest.ToList().IndexOf(currentPoi);
        if (pointsOfInterest.Count == 1 || currentIndex < 0)
        {
            return pointsOfInterest[Random.Shared.Next(pointsOfInterest.Count)];
        }

        var nextIndex = Random.Shared.Next(pointsOfInterest.Count - 1);
        if (nextIndex >= currentIndex)
        {
            nextIndex++;
        }

        return pointsOfInterest[nextIndex];
    }

    private void OnPictureSizeChanged(object? sender, EventArgs e)
    {
        UpdateSelectedPoiMarker();
    }

    private async void OnMarkerOverlayPressed(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(MarkerOverlay);
        if (position is null)
        {
            return;
        }

        var touchPosition = position.Value;
        var rect = GetImageRenderRectangle();

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var xPercent = (touchPosition.X - rect.Left) / rect.Width;
        var yPercent = 1 - ((touchPosition.Y - rect.Top) / rect.Height);

        if (xPercent < 0 || xPercent > 1 || yPercent < 0 || yPercent > 1)
        {
            return;
        }

        if (_isRandomMode)
        {
            if (_randomPoi is null)
            {
                return;
            }

            _arrowDrawable.ImageRect = rect;
            _arrowDrawable.GuessPosition = new PointF((float)xPercent, (float)yPercent);
            _arrowDrawable.SelectedPoi = _randomPoi;
            MarkerOverlay.Invalidate();
            return;
        }

        var text = await DisplayPromptAsync("Add point of interest", "Description");
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var poi = new PointOfInterest
        {
            Text = text.Trim(),
            XPercent = Math.Clamp(xPercent, 0, 1),
            YPercent = Math.Clamp(yPercent, 0, 1)
        };

        _pointsOfInterest.Add(poi);
        SavePointsOfInterest();
        if (!_isRandomMode)
        {
            SelectPoi(poi);
        }

        UpdatePoiList();
    }

    private async Task DeletePoiAsync(PointOfInterest? poiToRemove)
    {
        if (poiToRemove is null)
        {
            return;
        }

        var shouldDelete = await DisplayAlertAsync("Delete point", $"Delete '{poiToRemove.Text}'?", "Yes", "No");
        if (!shouldDelete)
        {
            return;
        }

        _pointsOfInterest.Remove(poiToRemove);
        SavePointsOfInterest();
        _selectedPoi = null;
        _arrowDrawable.SelectedPoi = null;
        _arrowDrawable.GuessPosition = null;
        if (_randomPoi == poiToRemove)
        {
            _randomPoi = null;
        }

        MarkerOverlay.Invalidate();
        UpdatePoiList();
    }

    private async void OnPoiDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: PointOfInterest poi })
        {
            await DeletePoiAsync(poi);
        }
    }

    private void OnPoiSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPoiList)
        {
            return;
        }

        if (e.CurrentSelection.FirstOrDefault() is not PointOfInterest selectedPoi)
        {
            _selectedPoi = null;
            _arrowDrawable.SelectedPoi = null;
            _arrowDrawable.GuessPosition = null;
            MarkerOverlay.Invalidate();
            return;
        }

        SelectPoi(selectedPoi);
    }

    private void SelectPoi(PointOfInterest poi)
    {
        _selectedPoi = poi;
        _arrowDrawable.GuessPosition = null;
        _arrowDrawable.SelectedPoi = poi;
        UpdateSelectedPoiMarker();
    }

    private void UpdateSelectedPoiMarker()
    {
        var poiToDisplay = _isRandomMode && _arrowDrawable.GuessPosition is not null
            ? _randomPoi
            : _selectedPoi;
        if (poiToDisplay is null)
        {
            _arrowDrawable.SelectedPoi = null;
            MarkerOverlay.Invalidate();
            return;
        }

        _arrowDrawable.ImageRect = GetImageRenderRectangle();
        _arrowDrawable.SelectedPoi = poiToDisplay;
        MarkerOverlay.Invalidate();
    }

    private void SavePointsOfInterest()
    {
        var json = JsonSerializer.Serialize(_pointsOfInterest, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_pictureLibrary.GetTargetsPath(_activePictureFileName), json);
    }

    private Rect GetImageRenderRectangle()
    {
        var width = MarkerOverlay.Width;
        var height = MarkerOverlay.Height;
        if (width <= 0 || height <= 0)
        {
            return Rect.Zero;
        }

        if (_imagePixelWidth <= 0 || _imagePixelHeight <= 0)
        {
            return Rect.Zero;
        }

        var scale = Math.Min(width / _imagePixelWidth, height / _imagePixelHeight);
        var renderWidth = _imagePixelWidth * scale;
        var renderHeight = _imagePixelHeight * scale;
        var left = (width - renderWidth) / 2.0;
        var top = (height - renderHeight) / 2.0;

        return new Rect(left, top, renderWidth, renderHeight);
    }
}

public class ArrowDrawable : IDrawable
{
    public PointOfInterest? SelectedPoi { get; set; }
    public Rect ImageRect { get; set; }
    public PointF? GuessPosition { get; set; }
    public bool IsFlashing { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (ImageRect.Width <= 0 || ImageRect.Height <= 0)
        {
            return;
        }

        if (GuessPosition is PointF guessPosition)
        {
            DrawMarker(
                canvas,
                ImageRect.Left + (guessPosition.X * ImageRect.Width),
                ImageRect.Top + ((1 - guessPosition.Y) * ImageRect.Height),
                Colors.Orange,
                6,
                56);
        }

        if (SelectedPoi is null)
        {
            return;
        }

        var x = ImageRect.Left + (SelectedPoi.XPercent * ImageRect.Width);
        var y = ImageRect.Top + ((1 - SelectedPoi.YPercent) * ImageRect.Height);
        var lineWidth = IsFlashing ? 8 : 6;
        var correctLocationColor = GuessPosition is null ? Colors.Red : Colors.Green;
        var strokeColor = IsFlashing ? correctLocationColor.WithAlpha(0.2f) : correctLocationColor;
        DrawMarker(canvas, x, y, strokeColor, lineWidth, 56);
    }

    private void DrawMarker(ICanvas canvas, double x, double y, Color color, double lineWidth, double arrowHeight)
    {
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return;
        }

        if (ImageRect.Height < arrowHeight + 12 || ImageRect.Width < 50)
        {
            canvas.StrokeColor = color;
            canvas.StrokeSize = (float)lineWidth;
            canvas.FillColor = color;
            canvas.DrawEllipse((float)x - 6, (float)y - 6, 12, 12);
            return;
        }

        canvas.StrokeColor = color;
        canvas.StrokeSize = (float)lineWidth;
        canvas.StrokeLineCap = LineCap.Round;

        canvas.DrawLine((float)x, (float)(y - arrowHeight), (float)x, (float)y);
        canvas.DrawLine((float)x, (float)y, (float)(x - 16), (float)(y - 24));
        canvas.DrawLine((float)x, (float)y, (float)(x + 16), (float)(y - 24));
    }
}
