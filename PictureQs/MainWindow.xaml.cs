using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Text.Json;
using PictureQs.Models;

namespace PictureQs
{
    public partial class MainWindow : Window
    {
        private static readonly string ImagePath = ResolveImagePath();
        private static readonly string PoiFilePath = System.IO.Path.Combine(AppContext.BaseDirectory, "poi.json");
        private List<PointOfInterest> pointsOfInterest = new();

        public MainWindow()
        {
            InitializeComponent();
            LoadImage();
            LoadPointsOfInterest();
        }

        private static string ResolveImagePath()
        {
            var candidate = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "picture.png");
            var fullPath = System.IO.Path.GetFullPath(candidate);
            return File.Exists(fullPath) ? fullPath : "picture.png";
        }

        private void LoadImage()
        {
            var imageUri = new Uri(ImagePath, UriKind.Absolute);
            var bitmap = new BitmapImage(imageUri);
            ImageControl.Source = bitmap;
        }

        private void LoadPointsOfInterest()
        {
            if (File.Exists(PoiFilePath))
            {
                var json = File.ReadAllText(PoiFilePath);
                pointsOfInterest = JsonSerializer.Deserialize<List<PointOfInterest>>(json) ?? new List<PointOfInterest>();
                UpdatePoiList();
            }
            else
            {
                pointsOfInterest = new List<PointOfInterest>();
            }
        }

        private void UpdatePoiList()
        {
            PoiListView.ItemsSource = pointsOfInterest.Select(poi => poi.Text).ToList();
        }

        private void ImageControl_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var position = e.GetPosition(ImageControl);
            var imageRect = GetImageRenderRectangle();

            if (position.X < imageRect.Left || position.X > imageRect.Right ||
                position.Y < imageRect.Top || position.Y > imageRect.Bottom)
            {
                return;
            }

            var xPercent = (position.X - imageRect.Left) / imageRect.Width;
            var yPercent = 1 - ((position.Y - imageRect.Top) / imageRect.Height);

            var inputDialog = new InputDialog { Owner = this };
            if (inputDialog.ShowDialog() == true)
            {
                var poi = new PointOfInterest
                {
                    Text = inputDialog.InputText,
                    XPercent = xPercent,
                    YPercent = yPercent
                };
                pointsOfInterest.Add(poi);
                SavePointsOfInterest();
                UpdatePoiList();
            }
        }

        private void SavePointsOfInterest()
        {
            var json = JsonSerializer.Serialize(pointsOfInterest);
            File.WriteAllText(PoiFilePath, json);
        }

        private void PoiListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PoiListView.SelectedItem is string selectedText)
            {
                var poi = pointsOfInterest.FirstOrDefault(p => p.Text == selectedText);
                if (poi != null)
                {
                    RenderSelectedPoiMarker(poi);
                }
            }
            else
            {
                MarkerCanvas.Children.Clear();
            }
        }

        private void ImageControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            MarkerCanvas.Width = ImageControl.ActualWidth;
            MarkerCanvas.Height = ImageControl.ActualHeight;

            if (PoiListView.SelectedItem is string selectedText)
            {
                var poi = pointsOfInterest.FirstOrDefault(p => p.Text == selectedText);
                if (poi != null)
                {
                    RenderSelectedPoiMarker(poi);
                }
            }
        }

        private void RenderSelectedPoiMarker(PointOfInterest poi)
        {
            var imageRect = GetImageRenderRectangle();
            if (imageRect.Width <= 0 || imageRect.Height <= 0)
            {
                return;
            }

            var x = Math.Clamp(imageRect.Left + (poi.XPercent * imageRect.Width), imageRect.Left + 24, imageRect.Right - 24);
            var y = Math.Clamp(imageRect.Top + ((1 - poi.YPercent) * imageRect.Height), imageRect.Top + 24, imageRect.Bottom - 24);

            MarkerCanvas.Children.Clear();

            var line = new Line
            {
                X1 = x,
                Y1 = y - 56,
                X2 = x,
                Y2 = y,
                Stroke = Brushes.Red,
                StrokeThickness = 6,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                SnapsToDevicePixels = true
            };

            var head = new Polygon
            {
                Fill = Brushes.Red,
                Stroke = Brushes.DarkRed,
                StrokeThickness = 2,
                Points = new PointCollection
                {
                    new Point(x, y),
                    new Point(x - 16, y - 24),
                    new Point(x + 16, y - 24)
                }
            };

            MarkerCanvas.Children.Add(line);
            MarkerCanvas.Children.Add(head);

            StartFlashAnimation();
        }

        private async void StartFlashAnimation()
        {
            if (MarkerCanvas.Children.Count == 0)
            {
                return;
            }

            foreach (var child in MarkerCanvas.Children.OfType<Shape>())
            {
                child.Opacity = 0.2;
            }

            await Task.Delay(70);

            foreach (var child in MarkerCanvas.Children.OfType<Shape>())
            {
                child.Opacity = 1.0;
            }
        }

        private (double Left, double Top, double Width, double Height, double Right, double Bottom) GetImageRenderRectangle()
        {
            var controlWidth = ImageControl.ActualWidth;
            var controlHeight = ImageControl.ActualHeight;
            if (controlWidth <= 0 || controlHeight <= 0 || ImageControl.Source is not BitmapSource source)
            {
                return (0, 0, 0, 0, 0, 0);
            }

            var sourceWidth = source.PixelWidth;
            var sourceHeight = source.PixelHeight;
            var scale = Math.Min(controlWidth / sourceWidth, controlHeight / sourceHeight);
            var renderWidth = sourceWidth * scale;
            var renderHeight = sourceHeight * scale;
            var left = (controlWidth - renderWidth) / 2.0;
            var top = (controlHeight - renderHeight) / 2.0;

            return (left, top, renderWidth, renderHeight, left + renderWidth, top + renderHeight);
        }
    }

    public class InputDialog : Window
    {
        private readonly TextBox inputTextBox;

        public InputDialog()
        {
            Title = "Add point of interest";
            Width = 320;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.White;

            var stackPanel = new StackPanel { Margin = new Thickness(12) };
            var label = new Label { Content = "Description" };
            inputTextBox = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                MaxLength = 40,
                FontSize = 14
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) =>
            {
                InputText = inputTextBox.Text.Trim();
                DialogResult = true;
                Close();
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                IsCancel = true
            };
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stackPanel.Children.Add(label);
            stackPanel.Children.Add(inputTextBox);
            stackPanel.Children.Add(buttonPanel);
            Content = stackPanel;
        }

        public string InputText { get; private set; } = string.Empty;
    }
}