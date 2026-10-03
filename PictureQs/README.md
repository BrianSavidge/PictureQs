# PictureQs Application

## Overview
PictureQs is a .NET application designed to load and display an image while allowing users to interact with it by recording points of interest. Users can right-click on the image to specify locations, enter text, and save this information in a JSON file for later retrieval.

## Features
- Loads and displays an image (`picture.png`) from the application folder.
- Resizes the image dynamically based on the application window size.
- Records the location of right-clicks on the image as percentages from the bottom left corner.
- Allows users to enter a text description (up to 40 characters) for each recorded point.
- Saves points of interest in a `poi.json` file, creating it if it doesn't exist.
- Loads existing points of interest from `poi.json` upon application startup.
- Displays a list of recorded points of interest, with the ability to highlight their locations on the image.

## Project Structure
```
PictureQs
├── Models
│   └── PointOfInterest.cs
├── Services
│   └── PoiStorageService.cs
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── PictureQs.csproj
└── README.md
```

## Setup Instructions
1. Clone the repository or download the project files.
2. Ensure you have the .NET SDK installed on your machine.
3. Open the project in your preferred IDE or editor.
4. Restore the project dependencies by running:
   ```
   dotnet restore
   ```
5. Build the project using:
   ```
   dotnet build
   ```
6. Run the application with:
   ```
   dotnet run
   ```

## Usage
- Upon launching the application, the image will be displayed on the left side of the window.
- Right-click on the image to record a point of interest.
- Enter a description in the text box that appears and click OK to save it.
- The list of points of interest will be displayed on the right side of the window.
- Click on any entry in the list to highlight its corresponding location on the image.

## Contributing
Contributions are welcome! Please feel free to submit a pull request or open an issue for any suggestions or improvements.

## License
This project is licensed under the MIT License. See the LICENSE file for more details.