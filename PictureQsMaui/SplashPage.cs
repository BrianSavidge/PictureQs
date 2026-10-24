namespace PictureQsMaui;

public sealed class SplashPage : ContentPage
{
    private const string AppName = "PictureQs";
    private const double TextWidthRatio = 0.95;
    private const double ArrowHeightRatio = 0.25;
    private const double VerticalShiftRatio = 0.20;
    private readonly HorizontalStackLayout _title;
    private readonly Label[] _letters;
    private readonly GraphicsView _arrow;
    private bool _animationStarted;

    public SplashPage()
    {
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = Color.FromArgb("#002417");

        _letters = AppName.Select(character => new Label
        {
            Text = character.ToString(),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
            FontAutoScalingEnabled = false,
            Padding = 0,
            Opacity = 0
        }).ToArray();
        _title = new HorizontalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        foreach (var letter in _letters)
        {
            _title.Children.Add(letter);
        }

        _arrow = new GraphicsView
        {
            Drawable = new ArrowDrawable(),
            IsVisible = false
        };

        var layout = new AbsoluteLayout { BackgroundColor = Color.FromArgb("#002417") };
        AbsoluteLayout.SetLayoutBounds(_arrow, new Rect(0, 0, 56, 100));
        layout.Children.Add(_title);
        layout.Children.Add(_arrow);
        layout.SizeChanged += OnLayoutSizeChanged;
        Content = layout;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_animationStarted)
        {
            return;
        }

        _animationStarted = true;
        for (var index = 0; index < AppName.Length; index++)
        {
            _letters[index].Opacity = 1;
            if (index < AppName.Length - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2));
            }
        }

        _arrow.IsVisible = true;
        await Task.Delay(TimeSpan.FromSeconds(0.6));

        var mainPage = new MainPage();
        NavigationPage.SetHasNavigationBar(mainPage, false);
        await Navigation.PushAsync(mainPage, animated: false);
        Navigation.RemovePage(this);
    }

    private void OnLayoutSizeChanged(object? sender, EventArgs e)
    {
        if (sender is not AbsoluteLayout layout || layout.Width <= 0 || layout.Height <= 0)
        {
            return;
        }

        var fontSize = layout.Width * 0.20;
        foreach (var letter in _letters)
        {
            letter.FontSize = fontSize;
        }

        var naturalWidth = _letters.Sum(letter =>
            letter.Measure(double.PositiveInfinity, double.PositiveInfinity).Width);
        fontSize *= layout.Width * TextWidthRatio / naturalWidth;
        foreach (var letter in _letters)
        {
            letter.FontSize = fontSize;
        }

        var textSize = _title.Measure(layout.Width * TextWidthRatio, layout.Height);
        var textTop = (layout.Height - textSize.Height) / 2 - layout.Height * VerticalShiftRatio;
        var iCenterX = layout.Width * (1 - TextWidthRatio) / 2
            + _letters[0].Measure(double.PositiveInfinity, double.PositiveInfinity).Width
            + _letters[1].Measure(double.PositiveInfinity, double.PositiveInfinity).Width / 2;
        AbsoluteLayout.SetLayoutBounds(
            _title,
            new Rect(layout.Width * (1 - TextWidthRatio) / 2, textTop, layout.Width * TextWidthRatio, textSize.Height));
        AbsoluteLayout.SetLayoutBounds(
            _arrow,
            new Rect(iCenterX - 28, textTop - layout.Height * ArrowHeightRatio, 56, layout.Height * ArrowHeightRatio));
    }

    private sealed class ArrowDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var centerX = dirtyRect.Width / 2;
            canvas.StrokeColor = Colors.Red;
            canvas.StrokeSize = 16;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawLine(centerX, 0, centerX, dirtyRect.Height);
            canvas.DrawLine(centerX, dirtyRect.Height, centerX - dirtyRect.Width * 0.4f, dirtyRect.Height * 0.62f);
            canvas.DrawLine(centerX, dirtyRect.Height, centerX + dirtyRect.Width * 0.4f, dirtyRect.Height * 0.62f);
        }
    }
}
