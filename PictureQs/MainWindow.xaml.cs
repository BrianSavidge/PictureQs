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
        private static readonly string AppDataFolder = AppContext.BaseDirectory;
        private static readonly string ImagePath = System.IO.Path.Combine(AppDataFolder, "picture.png");
        private static readonly string PoiFilePath = System.IO.Path.Combine(AppDataFolder, "poi.json");
        private List<PointOfInterest> pointsOfInterest = new();

        public MainWindow()
        {
            InitializeComponent();
            LoadImage();
            LoadPointsOfInterest();
        }

        private void LoadImage()
        {
            if (!File.Exists(ImagePath))
            {
                return;
            }

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
            var sortedItems = pointsOfInterest
                .Select(poi => poi.Text)
                .OrderBy(text => text, StringComparer.OrdinalIgnoreCase)
                .ToList();

            PoiListView.ItemsSource = sortedItems;
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
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(pointsOfInterest, options);
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

        private void PoiListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var clickedElement = (DependencyObject)e.OriginalSource;
            var listItem = FindAncestor<ListBoxItem>(clickedElement);

            var selectedText = listItem?.Content as string;
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                return;
            }

            var confirm = MessageBox.Show($"Delete '{selectedText}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                var poiToRemove = pointsOfInterest.FirstOrDefault(p => p.Text == selectedText);
                if (poiToRemove != null)
                {
                    pointsOfInterest.Remove(poiToRemove);
                    SavePointsOfInterest();
                    UpdatePoiList();
                    MarkerCanvas.Children.Clear();
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : class
        {
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
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
            if (double.IsNaN(imageRect.Left) || double.IsNaN(imageRect.Top) ||
                double.IsNaN(imageRect.Width) || double.IsNaN(imageRect.Height) ||
                imageRect.Width <= 0 || imageRect.Height <= 0)
            {
                MarkerCanvas.Children.Clear();
                return;
            }

            var maxX = Math.Max(0, imageRect.Right - 24);
            var maxY = Math.Max(0, imageRect.Bottom - 24);
            var minX = Math.Min(imageRect.Left + 24, maxX);
            var minY = Math.Min(imageRect.Top + 24, maxY);

            var x = Math.Clamp(imageRect.Left + (poi.XPercent * imageRect.Width), minX, maxX);
            var y = Math.Clamp(imageRect.Top + ((1 - poi.YPercent) * imageRect.Height), minY, maxY);

            MarkerCanvas.Children.Clear();

            var arrowHeight = 56.0;
            if (imageRect.Height < arrowHeight + 18 || imageRect.Width < 40 || double.IsInfinity(x) || double.IsInfinity(y))
            {
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = Brushes.Red,
                    Stroke = Brushes.DarkRed,
                    StrokeThickness = 1
                };

                Canvas.SetLeft(dot, x - 4);
                Canvas.SetTop(dot, y - 4);
                MarkerCanvas.Children.Add(dot);
                return;
            }

            var line = new Line
            {
                X1 = x,
                Y1 = y - arrowHeight,
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

            Loaded += (_, __) =>
            {
                inputTextBox.Focus();
                inputTextBox.SelectAll();
            };
        }

        public string InputText { get; private set; } = string.Empty;
    }
}