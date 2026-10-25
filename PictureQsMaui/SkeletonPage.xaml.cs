using System.Numerics;
using System.Text.Json;
using System.Windows.Input;
using PictureQsMaui.Models;

namespace PictureQsMaui;

public partial class SkeletonPage : ContentPage
{
    private readonly List<SkeletonPointOfInterest> _pointsOfInterest = new();
    private readonly SkeletonDrawable _skeletonDrawable = new();
    private readonly string _storagePath = Path.Combine(FileSystem.AppDataDirectory, "skeleton-poi.json");
    private readonly PanGestureRecognizer _panGesture = new();
    private readonly PinchGestureRecognizer _pinchGesture = new();
    private SkeletonPointOfInterest? _selectedPoi;
    private SkeletonPointOfInterest? _randomPoi;
    private Vector3? _guessPosition;
    private bool _isRandomMode;
    private bool _isUpdatingPoiList;
    private double _panStartYaw;
    private double _panStartPitch;
    private float _pinchStartZoom = 1;

    public ICommand DeletePoiCommand { get; }

    public SkeletonPage()
    {
        DeletePoiCommand = new Command<SkeletonPointOfInterest>(
            async poi => await DeletePoiAsync(poi));
        InitializeComponent();
        BindingContext = this;
        SkeletonCanvas.Drawable = _skeletonDrawable;
        _panGesture.PanUpdated += OnSkeletonPanned;
        _pinchGesture.PinchUpdated += OnSkeletonPinched;
        GestureSurface.GestureRecognizers.Add(_panGesture);
        GestureSurface.GestureRecognizers.Add(_pinchGesture);

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnSkeletonTapped;
        GestureSurface.GestureRecognizers.Add(tapGesture);

        LoadPointsOfInterest();
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
        UpdateDrawable();
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

    private void UpdateDrawable()
    {
        _skeletonDrawable.SelectedPoi = _isRandomMode ? _randomPoi : _selectedPoi;
        _skeletonDrawable.GuessPosition = _guessPosition;
        _skeletonDrawable.IsGuessCorrect = _guessPosition is not null
            && _randomPoi is not null
            && Distance(_randomPoi, _guessPosition.Value) <= 0.65f;
        SkeletonCanvas.Invalidate();
    }

    private static float Distance(SkeletonPointOfInterest poi, Vector3 point)
    {
        return Vector3.Distance(new Vector3(poi.X, poi.Y, poi.Z), point);
    }

    private void OnPoiSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPoiList)
        {
            return;
        }

        _selectedPoi = e.CurrentSelection.FirstOrDefault() as SkeletonPointOfInterest;
        UpdateDrawable();
    }

    private async void OnSkeletonTapped(object? sender, TappedEventArgs e)
    {
        var position = e.GetPosition(GestureSurface);
        if (position is null || !_skeletonDrawable.TryFindBonePoint(
                new PointF((float)position.Value.X, (float)position.Value.Y),
                new SizeF((float)GestureSurface.Width, (float)GestureSurface.Height),
                out var bonePoint))
        {
            return;
        }

        if (_isRandomMode)
        {
            _guessPosition = bonePoint;
            UpdateDrawable();
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
            X = bonePoint.X,
            Y = bonePoint.Y,
            Z = bonePoint.Z
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

    private void OnViewClicked(object? sender, EventArgs e)
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
    }

    private void OnRandomClicked(object? sender, EventArgs e)
    {
        var sorted = _pointsOfInterest
            .OrderBy(poi => poi.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _randomPoi = SelectRandomPoi(sorted, _randomPoi);
        _guessPosition = null;
        UpdatePoiList();
    }

    private async void OnPicturesClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private void OnZoomInClicked(object? sender, EventArgs e)
    {
        _skeletonDrawable.Zoom = Math.Min(_skeletonDrawable.Zoom * 1.25f, 4);
        SkeletonCanvas.Invalidate();
    }

    private void OnZoomOutClicked(object? sender, EventArgs e)
    {
        _skeletonDrawable.Zoom = Math.Max(_skeletonDrawable.Zoom / 1.25f, 0.6f);
        SkeletonCanvas.Invalidate();
    }

    private void OnResetViewClicked(object? sender, EventArgs e)
    {
        _skeletonDrawable.ResetView();
        SkeletonCanvas.Invalidate();
    }

    private void OnSkeletonPanned(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartYaw = _skeletonDrawable.Yaw;
                _panStartPitch = _skeletonDrawable.Pitch;
                break;
            case GestureStatus.Running:
                _skeletonDrawable.Yaw = _panStartYaw - (e.TotalX * 0.008);
                _skeletonDrawable.Pitch = Math.Clamp(
                    _panStartPitch + (e.TotalY * 0.008),
                    -1.25,
                    1.25);
                SkeletonCanvas.Invalidate();
                break;
        }
    }

    private void OnSkeletonPinched(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _pinchStartZoom = _skeletonDrawable.Zoom;
                break;
            case GestureStatus.Running:
                _skeletonDrawable.Zoom = Math.Clamp(_pinchStartZoom * (float)e.Scale, 0.6f, 4f);
                SkeletonCanvas.Invalidate();
                break;
        }
    }
}

internal sealed class SkeletonDrawable : IDrawable
{
    private readonly List<Bone> _bones = new();
    private readonly List<Vector3> _joints = new();

    public SkeletonPointOfInterest? SelectedPoi { get; set; }
    public Vector3? GuessPosition { get; set; }
    public bool IsGuessCorrect { get; set; }
    public float Zoom { get; set; } = 1;
    public double Yaw { get; set; } = 0.18;
    public double Pitch { get; set; } = 0.05;

    public SkeletonDrawable()
    {
        BuildSkeleton();
    }

    public void ResetView()
    {
        Zoom = 1;
        Yaw = 0.18;
        Pitch = 0.05;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var scale = GetScale(dirtyRect.Size);
        canvas.StrokeLineCap = LineCap.Round;

        foreach (var bone in _bones.OrderBy(bone => Transform(bone.Start).Z + Transform(bone.End).Z))
        {
            var start = Project(bone.Start, dirtyRect.Size, scale);
            var end = Project(bone.End, dirtyRect.Size, scale);
            var width = Math.Clamp(bone.Radius * scale * 2, 2.5f, 18);

            canvas.StrokeColor = Color.FromArgb("#24282B");
            canvas.StrokeSize = width + 2;
            canvas.DrawLine(start, end);
            canvas.StrokeColor = Color.FromArgb("#E6DFD0");
            canvas.StrokeSize = width;
            canvas.DrawLine(start, end);
        }

        foreach (var joint in _joints)
        {
            var point = Project(joint, dirtyRect.Size, scale);
            var radius = Math.Clamp(scale * 0.105f, 2.5f, 7);
            canvas.FillColor = Color.FromArgb("#E6DFD0");
            canvas.FillEllipse(point.X - radius, point.Y - radius, radius * 2, radius * 2);
        }

        if (SelectedPoi is not null)
        {
            DrawMarker(
                canvas,
                Project(new Vector3(SelectedPoi.X, SelectedPoi.Y, SelectedPoi.Z), dirtyRect.Size, scale),
                Colors.Red);
        }

        if (GuessPosition is Vector3 guess)
        {
            DrawMarker(
                canvas,
                Project(guess, dirtyRect.Size, scale),
                IsGuessCorrect ? Colors.LimeGreen : Colors.Orange);
        }
    }

    public bool TryFindBonePoint(PointF tap, SizeF viewport, out Vector3 point)
    {
        var scale = GetScale(viewport);
        var nearestDistance = 22f;
        var nearestPoint = Vector3.Zero;

        foreach (var bone in _bones)
        {
            var start = Project(bone.Start, viewport, scale);
            var end = Project(bone.End, viewport, scale);
            var deltaX = end.X - start.X;
            var deltaY = end.Y - start.Y;
            var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
            var fraction = lengthSquared <= float.Epsilon
                ? 0
                : Math.Clamp(((tap.X - start.X) * deltaX + (tap.Y - start.Y) * deltaY) / lengthSquared, 0, 1);
            var closestX = start.X + (fraction * deltaX);
            var closestY = start.Y + (fraction * deltaY);
            var distanceX = tap.X - closestX;
            var distanceY = tap.Y - closestY;
            var distance = MathF.Sqrt((distanceX * distanceX) + (distanceY * distanceY));
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPoint = Vector3.Lerp(bone.Start, bone.End, fraction);
            }
        }

        point = nearestPoint;
        return nearestDistance < 22f;
    }

    private float GetScale(SizeF size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return 1;
        }

        return Math.Min(size.Width / 5.2f, size.Height / 13f) * 0.82f * Zoom;
    }

    private PointF Project(Vector3 point, SizeF viewport, float scale)
    {
        var transformed = Transform(point);
        return new PointF(
            (viewport.Width / 2) + (transformed.X * scale),
            (viewport.Height / 2) - (transformed.Y * scale));
    }

    private Vector3 Transform(Vector3 point)
    {
        var cosineYaw = (float)Math.Cos(Yaw);
        var sineYaw = (float)Math.Sin(Yaw);
        var rotatedX = (point.X * cosineYaw) + (point.Z * sineYaw);
        var rotatedZ = (-point.X * sineYaw) + (point.Z * cosineYaw);
        var cosinePitch = (float)Math.Cos(Pitch);
        var sinePitch = (float)Math.Sin(Pitch);
        return new Vector3(
            rotatedX,
            (point.Y * cosinePitch) - (rotatedZ * sinePitch),
            (point.Y * sinePitch) + (rotatedZ * cosinePitch));
    }

    private static void DrawMarker(ICanvas canvas, PointF point, Color color)
    {
        const float radius = 9;
        canvas.FillColor = color.WithAlpha(0.28f);
        canvas.FillEllipse(point.X - radius * 1.8f, point.Y - radius * 1.8f, radius * 3.6f, radius * 3.6f);
        canvas.FillColor = color;
        canvas.FillEllipse(point.X - radius, point.Y - radius, radius * 2, radius * 2);
        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 2;
        canvas.DrawEllipse(point.X - radius, point.Y - radius, radius * 2, radius * 2);
    }

    private void BuildSkeleton()
    {
        AddSkull();
        AddSpineAndRibs();
        AddShoulderGirdleAndArms();
        AddPelvisAndLegs();
    }

    private void AddSkull()
    {
        const int sides = 16;
        var levels = new[]
        {
            (Y: 7.25f, RadiusX: 0.12f, RadiusZ: 0.12f),
            (Y: 7.05f, RadiusX: 0.38f, RadiusZ: 0.32f),
            (Y: 6.72f, RadiusX: 0.48f, RadiusZ: 0.40f),
            (Y: 6.38f, RadiusX: 0.42f, RadiusZ: 0.37f),
            (Y: 6.08f, RadiusX: 0.30f, RadiusZ: 0.29f)
        };
        var rings = levels.Select(level => Enumerable.Range(0, sides)
            .Select(index =>
            {
                var angle = (MathF.PI * 2 * index) / sides;
                return new Vector3(
                    MathF.Cos(angle) * level.RadiusX,
                    level.Y,
                    MathF.Sin(angle) * level.RadiusZ);
            }).ToArray()).ToArray();

        foreach (var ring in rings)
        {
            AddPolyline(ring, 0.045f, true);
        }

        for (var level = 0; level < rings.Length - 1; level++)
        {
            for (var side = 0; side < sides; side += 2)
            {
                AddBone(rings[level][side], rings[level + 1][side], 0.035f);
            }
        }

        AddPolyline(new[]
        {
            new Vector3(-0.29f, 6.23f, 0.22f),
            new Vector3(-0.18f, 6.05f, 0.31f),
            new Vector3(0, 6.01f, 0.34f),
            new Vector3(0.18f, 6.05f, 0.31f),
            new Vector3(0.29f, 6.23f, 0.22f)
        }, 0.055f);
        AddBone(new Vector3(0, 6.69f, 0.37f), new Vector3(0, 6.36f, 0.42f), 0.055f);
        AddBone(new Vector3(0, 6.36f, 0.42f), new Vector3(-0.12f, 6.23f, 0.37f), 0.045f);
        AddBone(new Vector3(0, 6.36f, 0.42f), new Vector3(0.12f, 6.23f, 0.37f), 0.045f);
        AddEyeSocket(-0.19f);
        AddEyeSocket(0.19f);
        AddBone(new Vector3(0, 6.08f, 0.12f), new Vector3(0, 5.68f, 0.05f), 0.12f);
        AddBone(new Vector3(-0.26f, 6.08f, 0.12f), new Vector3(-0.19f, 5.88f, 0.12f), 0.07f);
        AddBone(new Vector3(0.26f, 6.08f, 0.12f), new Vector3(0.19f, 5.88f, 0.12f), 0.07f);
    }

    private void AddEyeSocket(float centerX)
    {
        var points = Enumerable.Range(0, 13)
            .Select(index =>
            {
                var angle = (MathF.PI * 2 * index) / 12;
                return new Vector3(
                    centerX + (MathF.Cos(angle) * 0.115f),
                    6.55f + (MathF.Sin(angle) * 0.105f),
                    0.34f);
            }).ToArray();
        AddPolyline(points, 0.035f);
    }

    private void AddSpineAndRibs()
    {
        var vertebrae = new List<Vector3>();
        for (var index = 0; index < 25; index++)
        {
            var y = 5.0f - (index * 0.205f);
            var depth = y > 2.7f ? -0.08f : 0.02f;
            var vertebra = new Vector3(0, y, depth);
            vertebrae.Add(vertebra);
            AddBone(
                new Vector3(-0.12f, y, depth),
                new Vector3(0.12f, y, depth),
                y > 3.1f ? 0.085f : 0.11f);
            if (index > 0)
            {
                AddBone(vertebrae[index - 1], vertebra, 0.07f);
            }
        }

        AddBone(new Vector3(0, 5.0f, -0.05f), new Vector3(0, 5.9f, 0), 0.16f);

        for (var rib = 0; rib < 12; rib++)
        {
            var y = 4.65f - (rib * 0.19f);
            var width = rib < 7 ? 0.92f - (rib * 0.025f) : 0.75f - ((rib - 7) * 0.11f);
            for (var side = -1; side <= 1; side += 2)
            {
                var sign = side;
                var ribPoints = Enumerable.Range(0, 9)
                    .Select(index =>
                    {
                        var t = index / 8f;
                        var x = sign * (0.12f + (width * MathF.Sin(t * MathF.PI * 0.5f)));
                        var ribY = y - (0.12f * MathF.Sin(t * MathF.PI));
                        var z = -0.08f + (0.72f * t);
                        return new Vector3(x, ribY, z);
                    }).ToArray();
                AddPolyline(ribPoints, 0.045f);
                if (rib < 7)
                {
                    AddBone(ribPoints[^1], new Vector3(0, y - 0.12f, 0.68f), 0.04f);
                }
            }
        }

        AddBone(new Vector3(0, 4.65f, 0.69f), new Vector3(0, 2.9f, 0.70f), 0.13f);
        AddBone(new Vector3(0, 5.0f, -0.05f), new Vector3(0, 5.55f, -0.05f), 0.12f);
    }

    private void AddShoulderGirdleAndArms()
    {
        foreach (var side in new[] { -1f, 1f })
        {
            AddPolyline(new[]
            {
                new Vector3(0, 4.75f, 0.48f),
                new Vector3(side * 0.38f, 4.82f, 0.46f),
                new Vector3(side * 0.75f, 4.70f, 0.34f),
                new Vector3(side * 0.94f, 4.55f, 0.18f)
            }, 0.10f);
            AddPolyline(new[]
            {
                new Vector3(side * 0.42f, 4.64f, -0.25f),
                new Vector3(side * 0.88f, 4.48f, -0.32f),
                new Vector3(side * 1.00f, 4.12f, -0.30f),
                new Vector3(side * 0.67f, 3.98f, -0.24f),
                new Vector3(side * 0.42f, 4.64f, -0.25f)
            }, 0.065f);

            var shoulder = new Vector3(side * 0.98f, 4.48f, 0.05f);
            var elbow = new Vector3(side * 1.15f, 2.95f, 0.02f);
            var wrist = new Vector3(side * 1.22f, 1.52f, 0.05f);
            AddBone(shoulder, elbow, 0.16f);
            AddBone(elbow, wrist, 0.075f);
            AddBone(
                new Vector3(side * 1.28f, 2.95f, 0.08f),
                new Vector3(side * 1.30f, 1.54f, -0.08f),
                0.07f);
            AddBone(new Vector3(side * 1.2f, 3.02f, 0.15f), new Vector3(side * 1.31f, 2.88f, 0.15f), 0.12f);
            AddBone(wrist, new Vector3(side * 1.25f, 1.12f, 0.10f), 0.09f);

            for (var finger = 0; finger < 4; finger++)
            {
                var startX = side * (1.05f + (finger * 0.13f));
                var endX = side * (1.01f + (finger * 0.16f));
                var root = new Vector3(startX, 1.12f, 0.10f);
                var middle = new Vector3((startX + endX) / 2, 0.77f, 0.12f);
                var tip = new Vector3(endX, 0.40f - (MathF.Abs(finger - 1.5f) * 0.05f), 0.12f);
                AddBone(root, middle, 0.045f);
                AddBone(middle, tip, 0.035f);
                AddBone(tip, new Vector3(endX, tip.Y - 0.18f, 0.13f), 0.027f);
            }

            var thumbBase = new Vector3(side * 1.05f, 1.23f, 0.12f);
            var thumbMiddle = new Vector3(side * 0.88f, 1.02f, 0.20f);
            var thumbTip = new Vector3(side * 0.82f, 0.78f, 0.22f);
            AddBone(thumbBase, thumbMiddle, 0.05f);
            AddBone(thumbMiddle, thumbTip, 0.04f);
        }
    }

    private void AddPelvisAndLegs()
    {
        foreach (var side in new[] { -1f, 1f })
        {
            AddPolyline(new[]
            {
                new Vector3(0, 0.15f, -0.05f),
                new Vector3(side * 0.35f, 0.25f, -0.08f),
                new Vector3(side * 0.72f, 0.10f, 0.03f),
                new Vector3(side * 0.82f, -0.30f, 0.12f),
                new Vector3(side * 0.57f, -0.62f, 0.20f)
            }, 0.13f);
            AddBone(new Vector3(0, 0.12f, 0.40f), new Vector3(side * 0.42f, -0.10f, 0.44f), 0.09f);
            AddBone(new Vector3(side * 0.42f, -0.10f, 0.44f), new Vector3(side * 0.57f, -0.62f, 0.20f), 0.09f);

            var hip = new Vector3(side * 0.57f, -0.55f, 0.05f);
            var knee = new Vector3(side * 0.62f, -2.48f, 0.03f);
            var ankle = new Vector3(side * 0.62f, -4.35f, 0.02f);
            AddBone(hip, knee, 0.20f);
            AddBone(knee, ankle, 0.105f);
            AddBone(
                new Vector3(side * 0.78f, -2.48f, -0.08f),
                new Vector3(side * 0.74f, -4.30f, -0.07f),
                0.07f);
            AddPolyline(new[]
            {
                new Vector3(side * 0.62f, -2.34f, 0.16f),
                new Vector3(side * 0.73f, -2.44f, 0.28f),
                new Vector3(side * 0.65f, -2.58f, 0.18f)
            }, 0.07f);

            var heel = new Vector3(side * 0.62f, -4.42f, -0.28f);
            var midFoot = new Vector3(side * 0.62f, -4.62f, 0.30f);
            AddBone(ankle, heel, 0.09f);
            for (var toe = 0; toe < 5; toe++)
            {
                var toeX = side * (0.42f + (toe * 0.10f));
                var toeBase = new Vector3(toeX, -4.63f, 0.28f);
                var toeTip = new Vector3(toeX, -4.69f, 0.80f - (MathF.Abs(toe - 1) * 0.08f));
                AddBone(midFoot, toeBase, 0.045f);
                AddBone(toeBase, toeTip, 0.035f);
            }
        }
    }

    private void AddPolyline(IReadOnlyList<Vector3> points, float radius, bool close = false)
    {
        for (var index = 1; index < points.Count; index++)
        {
            AddBone(points[index - 1], points[index], radius);
        }

        if (close && points.Count > 2)
        {
            AddBone(points[^1], points[0], radius);
        }
    }

    private void AddBone(Vector3 start, Vector3 end, float radius)
    {
        _bones.Add(new Bone(start, end, radius));
        _joints.Add(start);
        _joints.Add(end);
    }

    private readonly record struct Bone(Vector3 Start, Vector3 End, float Radius);
}
