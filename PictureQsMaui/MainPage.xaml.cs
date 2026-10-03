using System.Buffers.Binary;
using System.Text.Json;
using System.Windows.Input;
using PictureQsMaui.Models;

namespace PictureQsMaui;

public partial class MainPage : ContentPage
{
    private readonly List<PointOfInterest> _pointsOfInterest = new();
    private readonly string _imagePath = Path.Combine(FileSystem.CacheDirectory, "picture.png");
    private readonly string _poiFilePath = Path.Combine(AppContext.BaseDirectory, "poi.json");
    private readonly ArrowDrawable _arrowDrawable = new();
    private PointOfInterest? _selectedPoi;
    private int _imagePixelWidth;
    private int _imagePixelHeight;

    public ICommand DeletePoiCommand { get; }

    public MainPage()
    {
        DeletePoiCommand = new Command<string>(async text => await DeletePoiAsync(text));
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
        await LoadImageAsync();
    }

    private async Task LoadImageAsync()
    {
        await using (var source = await FileSystem.OpenAppPackageFileAsync("picture.png"))
        await using (var destination = File.Create(_imagePath))
        {
            await source.CopyToAsync(destination);
        }

        var header = new byte[24];
        await using (var stream = File.OpenRead(_imagePath))
        {
            await stream.ReadExactlyAsync(header);
        }

        if (!header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            throw new InvalidDataException($"The picture at '{_imagePath}' is not a PNG image.");
        }

        _imagePixelWidth = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        _imagePixelHeight = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        if (_imagePixelWidth <= 0 || _imagePixelHeight <= 0)
        {
            throw new InvalidDataException($"The picture at '{_imagePath}' has invalid dimensions.");
        }

        PictureImage.Source = ImageSource.FromFile(_imagePath);
    }

    private void LoadPointsOfInterest()
    {
        if (File.Exists(_poiFilePath))
        {
            var json = File.ReadAllText(_poiFilePath);
            var points = JsonSerializer.Deserialize<List<PointOfInterest>>(json);
            if (points is not null)
            {
                _pointsOfInterest.Clear();
                _pointsOfInterest.AddRange(points);
            }
        }

        UpdatePoiList();
    }

    private void UpdatePoiList()
    {
        var sorted = _pointsOfInterest
            .OrderBy(p => p.Text, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Text)
            .ToList();

        PoiListView.ItemsSource = sorted;
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
        UpdatePoiList();
        SelectPoi(poi);
    }

    private async Task DeletePoiAsync(string? selectedText)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return;
        }

        var shouldDelete = await DisplayAlertAsync("Delete point", $"Delete '{selectedText}'?", "Yes", "No");
        if (!shouldDelete)
        {
            return;
        }

        var poiToRemove = _pointsOfInterest.FirstOrDefault(p => p.Text == selectedText);
        if (poiToRemove is null)
        {
            return;
        }

        _pointsOfInterest.Remove(poiToRemove);
        SavePointsOfInterest();
        UpdatePoiList();
        _selectedPoi = null;
        _arrowDrawable.SelectedPoi = null;
        MarkerOverlay.Invalidate();
        PoiListView.SelectedItem = null;
    }

    private async void OnPoiDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: string selectedText })
        {
            await DeletePoiAsync(selectedText);
        }
    }

    private void OnPoiSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not string selectedText)
        {
            _selectedPoi = null;
            _arrowDrawable.SelectedPoi = null;
            MarkerOverlay.Invalidate();
            return;
        }

        var foundPoi = _pointsOfInterest.FirstOrDefault(p => p.Text == selectedText);
        if (foundPoi is null)
        {
            return;
        }

        SelectPoi(foundPoi);
    }

    private void SelectPoi(PointOfInterest poi)
    {
        _selectedPoi = poi;
        _arrowDrawable.SelectedPoi = poi;
        UpdateSelectedPoiMarker();
    }

    private void UpdateSelectedPoiMarker()
    {
        if (_selectedPoi is null)
        {
            _arrowDrawable.SelectedPoi = null;
            MarkerOverlay.Invalidate();
            return;
        }

        _arrowDrawable.ImageRect = GetImageRenderRectangle();
        _arrowDrawable.SelectedPoi = _selectedPoi;
        MarkerOverlay.Invalidate();
    }

    private void SavePointsOfInterest()
    {
        var json = JsonSerializer.Serialize(_pointsOfInterest, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_poiFilePath, json);
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
    public bool IsFlashing { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (SelectedPoi is null)
        {
            return;
        }

        var x = ImageRect.Left + (SelectedPoi.XPercent * ImageRect.Width);
        var y = ImageRect.Top + ((1 - SelectedPoi.YPercent) * ImageRect.Height);

        if (double.IsNaN(x) || double.IsNaN(y) || ImageRect.Width <= 0 || ImageRect.Height <= 0)
        {
            return;
        }

        var arrowHeight = 56d;
        var lineWidth = IsFlashing ? 8 : 6;
        var strokeColor = IsFlashing ? Colors.Red.WithAlpha(0.2f) : Colors.Red;

        if (ImageRect.Height < arrowHeight + 12 || ImageRect.Width < 50)
        {
            canvas.StrokeColor = strokeColor;
            canvas.StrokeSize = 6;
            canvas.FillColor = strokeColor;
            canvas.DrawEllipse((float)x - 6, (float)y - 6, 12, 12);
            return;
        }

        canvas.StrokeColor = strokeColor;
        canvas.StrokeSize = lineWidth;
        canvas.StrokeLineCap = LineCap.Round;

        canvas.DrawLine((float)x, (float)(y - arrowHeight), (float)x, (float)y);
        canvas.DrawLine((float)x, (float)y, (float)(x - 16), (float)(y - 24));
        canvas.DrawLine((float)x, (float)y, (float)(x + 16), (float)(y - 24));
    }
}
