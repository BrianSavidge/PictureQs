using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Windows.Input;
using PictureQsMaui.Models;

namespace PictureQsMaui;

public partial class SkeletonPage : ContentPage
{
    private readonly List<SkeletonPointOfInterest> _pointsOfInterest = new();
    private readonly string _storagePath = Path.Combine(FileSystem.AppDataDirectory, "skeleton-poi.json");
    private SkeletonPointOfInterest? _selectedPoi;
    private SkeletonPointOfInterest? _randomPoi;
    private Vector3? _guessPosition;
    private bool _isRandomMode;
    private bool _isUpdatingPoiList;
    private bool _viewerReady;

    public ICommand DeletePoiCommand { get; }

    public SkeletonPage()
    {
        DeletePoiCommand = new Command<SkeletonPointOfInterest>(
            async poi => await DeletePoiAsync(poi));
        InitializeComponent();
        BindingContext = this;
        Loaded += OnPageLoaded;
        LoadPointsOfInterest();
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnPageLoaded;
        try
        {
            await using var viewerStream = await FileSystem.OpenAppPackageFileAsync("Skeleton/viewer.bundle.js");
            using var reader = new StreamReader(viewerStream);
            var viewerScript = await reader.ReadToEndAsync();
            SkeletonWebView.Source = new HtmlWebViewSource
            {
                BaseUrl = "https://appassets.androidplatform.net/",
                Html = $$"""
                         <!doctype html>
                         <html lang="en">
                         <head>
                           <meta charset="utf-8">
                           <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">
                           <style>
                             html, body { margin: 0; width: 100%; height: 100%; overflow: hidden; background: #101820; }
                             canvas { display: block; width: 100%; height: 100%; touch-action: none; }
                           </style>
                         </head>
                         <body><script>{{viewerScript}}</script></body>
                         </html>
                         """
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LoadingLabel.Text = "Could not load the skeleton viewer.";
            await DisplayAlertAsync("Could not load skeleton", ex.Message, "OK");
        }
    }

    private void OnSkeletonWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "pictureqs", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        switch (uri.Host)
        {
            case "ready":
                _viewerReady = true;
                _ = LoadModelIntoViewerAsync();
                break;
            case "loaded":
                LoadingLabel.IsVisible = false;
                _ = SendSceneStateAsync();
                break;
            case "tap":
                if (TryReadPosition(uri, out var position))
                {
                    _ = HandleModelTappedAsync(position);
                }

                break;
            case "error":
                LoadingLabel.Text = "Could not load the skeleton model.";
                LoadingLabel.IsVisible = true;
                _ = DisplayAlertAsync(
                    "Could not load skeleton",
                    "The 3D viewer could not load the skeleton model.",
                    "OK");
                break;
        }
    }

    private async Task LoadModelIntoViewerAsync()
    {
        try
        {
            await using var modelStream = await FileSystem.OpenAppPackageFileAsync("Skeleton/Human skeleton HD.glb");
            using var modelBuffer = new MemoryStream();
            await modelStream.CopyToAsync(modelBuffer);
            var modelBytes = modelBuffer.ToArray();
            const int chunkSize = 256 * 1024;
            var chunkCount = (modelBytes.Length + chunkSize - 1) / chunkSize;

            await SkeletonWebView.EvaluateJavaScriptAsync($"window.beginModelLoad({chunkCount})");
            for (var index = 0; index < chunkCount; index++)
            {
                var offset = index * chunkSize;
                var length = Math.Min(chunkSize, modelBytes.Length - offset);
                var chunk = Convert.ToBase64String(modelBytes, offset, length);
                var encodedChunk = JsonSerializer.Serialize(chunk);
                await SkeletonWebView.EvaluateJavaScriptAsync(
                    $"window.appendModelChunk({index},{encodedChunk})");
            }

            await SkeletonWebView.EvaluateJavaScriptAsync("window.finishModelLoading()");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LoadingLabel.Text = "Could not read the skeleton model.";
            LoadingLabel.IsVisible = true;
            await DisplayAlertAsync("Could not load skeleton", ex.Message, "OK");
        }
    }

    private static bool TryReadPosition(Uri uri, out Vector3 position)
    {
        var values = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));

        if (values.TryGetValue("x", out var x)
            && values.TryGetValue("y", out var y)
            && values.TryGetValue("z", out var z)
            && float.TryParse(x, CultureInfo.InvariantCulture, out var parsedX)
            && float.TryParse(y, CultureInfo.InvariantCulture, out var parsedY)
            && float.TryParse(z, CultureInfo.InvariantCulture, out var parsedZ)
            && float.IsFinite(parsedX)
            && float.IsFinite(parsedY)
            && float.IsFinite(parsedZ))
        {
            position = new Vector3(parsedX, parsedY, parsedZ);
            return true;
        }

        position = Vector3.Zero;
        return false;
    }

    private void LoadPointsOfInterest()
    {
        if (File.Exists(_storagePath))
        {
            var json = File.ReadAllText(_storagePath);
            var points = JsonSerializer.Deserialize<List<SkeletonPointOfInterest>>(json);
            if (points is not null)
            {
                _pointsOfInterest.AddRange(points);
            }
        }

        UpdatePoiList();
    }

    private void SavePointsOfInterest()
    {
        var json = JsonSerializer.Serialize(_pointsOfInterest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_storagePath, json);
    }

    private void UpdatePoiList()
    {
        var sorted = _pointsOfInterest
            .OrderBy(poi => poi.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_isRandomMode && (_randomPoi is null || !sorted.Contains(_randomPoi)))
        {
            _randomPoi = SelectRandomPoi(sorted, null);
        }

        _isUpdatingPoiList = true;
        try
        {
            PoiListView.ItemsSource = _isRandomMode
                ? _randomPoi is null ? Array.Empty<SkeletonPointOfInterest>() : new[] { _randomPoi }
                : sorted;
            PoiListView.SelectedItem = _isRandomMode ? null : _selectedPoi;
        }
        finally
        {
            _isUpdatingPoiList = false;
        }

        RandomButton.IsEnabled = sorted.Count > 1;
        _ = SendSceneStateAsync();
    }

    private static SkeletonPointOfInterest? SelectRandomPoi(
        IReadOnlyList<SkeletonPointOfInterest> points,
        SkeletonPointOfInterest? currentPoi)
    {
        if (points.Count == 0)
        {
            return null;
        }

        var currentIndex = currentPoi is null ? -1 : points.ToList().IndexOf(currentPoi);
        if (points.Count == 1 || currentIndex < 0)
        {
            return points[Random.Shared.Next(points.Count)];
        }

        var nextIndex = Random.Shared.Next(points.Count - 1);
        return points[nextIndex >= currentIndex ? nextIndex + 1 : nextIndex];
    }

    private async Task SendSceneStateAsync()
    {
        if (!_viewerReady)
        {
            return;
        }

        var selectedIndex = _isRandomMode
            ? -1
            : _pointsOfInterest.IndexOf(_selectedPoi!);
        var state = JsonSerializer.Serialize(new
        {
            points = _pointsOfInterest,
            selectedIndex,
            showPoints = !_isRandomMode,
            guess = _guessPosition is Vector3 guess
                ? new[] { guess.X, guess.Y, guess.Z }
                : null,
            guessCorrect = _isRandomMode
                && _randomPoi is not null
                && _guessPosition is Vector3 guessPosition
                && Distance(_randomPoi, guessPosition) <= 0.2f
        });
        await SkeletonWebView.EvaluateJavaScriptAsync($"window.setSceneState({state})");
    }

    private void OnPoiSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPoiList)
        {
            return;
        }

        _selectedPoi = e.CurrentSelection.FirstOrDefault() as SkeletonPointOfInterest;
        _ = SendSceneStateAsync();
    }

    private async Task HandleModelTappedAsync(Vector3 position)
    {
        if (_isRandomMode)
        {
            _guessPosition = position;
            await SendSceneStateAsync();
            return;
        }

        var text = await DisplayPromptAsync("Add point of interest", "Description");
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var poi = new SkeletonPointOfInterest
        {
            Text = text.Trim(),
            X = position.X,
            Y = position.Y,
            Z = position.Z
        };
        _pointsOfInterest.Add(poi);
        SavePointsOfInterest();
        _selectedPoi = poi;
        UpdatePoiList();
    }

    private async Task DeletePoiAsync(SkeletonPointOfInterest? poiToRemove)
    {
        if (poiToRemove is null)
        {
            return;
        }

        var shouldDelete = await DisplayAlertAsync(
            "Delete point",
            $"Delete '{poiToRemove.Text}'?",
            "Yes",
            "No");
        if (!shouldDelete)
        {
            return;
        }

        _pointsOfInterest.Remove(poiToRemove);
        SavePointsOfInterest();
        if (_selectedPoi == poiToRemove)
        {
            _selectedPoi = null;
        }

        if (_randomPoi == poiToRemove)
        {
            _randomPoi = null;
        }

        _guessPosition = null;
        UpdatePoiList();
    }

    private async void OnViewClicked(object? sender, EventArgs e)
    {
        _isRandomMode = !_isRandomMode;
        _guessPosition = null;
        ViewModeButton.Text = _isRandomMode ? "Edit Mode" : "Quiz Mode";
        RandomButton.IsVisible = _isRandomMode;
        if (_isRandomMode)
        {
            _randomPoi = SelectRandomPoi(
                _pointsOfInterest.OrderBy(poi => poi.Text, StringComparer.OrdinalIgnoreCase).ToList(),
                null);
        }

        UpdatePoiList();
        await SendSceneStateAsync();
    }

    private async void OnRandomClicked(object? sender, EventArgs e)
    {
        var sorted = _pointsOfInterest
            .OrderBy(poi => poi.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _randomPoi = SelectRandomPoi(sorted, _randomPoi);
        _guessPosition = null;
        UpdatePoiList();
        await SendSceneStateAsync();
    }

    private async void OnPicturesClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private static float Distance(SkeletonPointOfInterest poi, Vector3 point)
    {
        return Vector3.Distance(new Vector3(poi.X, poi.Y, poi.Z), point);
    }
}
