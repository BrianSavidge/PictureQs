using System.Text.Json;

namespace PictureQsMaui.Services;

public sealed class PictureLibraryService
{
    private const string SettingsFileName = ".picture-library.json";
    private readonly string _folderPath = FileSystem.AppDataDirectory;
    private readonly string _settingsFilePath;
    private readonly Dictionary<string, string> _targetFileNames = new(StringComparer.OrdinalIgnoreCase);

    public PictureLibraryService()
    {
        _settingsFilePath = Path.Combine(_folderPath, SettingsFileName);
    }

    public string SelectedPictureFileName { get; private set; } = "picture.png";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_folderPath);

        var defaultPicturePath = Path.Combine(_folderPath, "picture.png");
        if (!File.Exists(defaultPicturePath))
        {
            await using var source = await FileSystem.OpenAppPackageFileAsync("picture.png");
            await using var destination = File.Create(defaultPicturePath);
            await source.CopyToAsync(destination);
        }

        if (File.Exists(_settingsFilePath))
        {
            var settingsJson = await File.ReadAllTextAsync(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<PictureLibrarySettings>(settingsJson);
            if (settings is not null)
            {
                foreach (var pair in settings.TargetFileNames)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    {
                        _targetFileNames[pair.Key] = pair.Value;
                    }
                }

                if (GetPictureFileNames().Contains(settings.SelectedPictureFileName, StringComparer.OrdinalIgnoreCase))
                {
                    SelectedPictureFileName = settings.SelectedPictureFileName;
                }
            }
        }

        if (File.Exists(Path.Combine(_folderPath, "poi.json")) == false)
        {
            var legacyTargetsPath = Path.Combine(AppContext.BaseDirectory, "poi.json");
            if (File.Exists(legacyTargetsPath) &&
                !string.Equals(Path.GetFullPath(legacyTargetsPath), Path.GetFullPath(Path.Combine(_folderPath, "poi.json")), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(legacyTargetsPath, Path.Combine(_folderPath, "poi.json"));
            }
        }

        _targetFileNames.TryAdd("picture.png", "poi");
        foreach (var fileName in GetPictureFileNames())
        {
            _targetFileNames.TryAdd(fileName, Path.GetFileNameWithoutExtension(fileName));
        }
    }

    public IReadOnlyList<string> GetPictureFileNames()
    {
        return Directory.EnumerateFiles(_folderPath)
            .Where(IsPictureFile)
            .Select(Path.GetFileName)
            .Where(fileName => fileName is not null)
            .Cast<string>()
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string GetPicturePath(string pictureFileName)
    {
        return Path.Combine(_folderPath, GetSafeFileName(pictureFileName));
    }

    public string GetTargetsPath(string pictureFileName)
    {
        if (!_targetFileNames.TryGetValue(pictureFileName, out var targetsFileName))
        {
            targetsFileName = Path.GetFileNameWithoutExtension(pictureFileName);
        }

        return Path.Combine(_folderPath, $"{GetSafeFileName(targetsFileName)}.json");
    }

    public string GetTargetsFileName(string pictureFileName)
    {
        return _targetFileNames.TryGetValue(pictureFileName, out var targetsFileName)
            ? targetsFileName
            : Path.GetFileNameWithoutExtension(pictureFileName);
    }

    public async Task<string> ImportPictureAsync(FileResult photo)
    {
        var fileName = Path.GetFileName(photo.FileName);
        var extension = Path.GetExtension(fileName);
        if (!IsSupportedPictureExtension(extension))
        {
            throw new InvalidDataException($"The selected file type '{extension}' is not supported.");
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(invalidCharacter, '_');
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "picture";
        }

        var destinationPath = Path.Combine(_folderPath, $"{baseName}{extension}");
        var suffix = 2;
        while (File.Exists(destinationPath))
        {
            destinationPath = Path.Combine(_folderPath, $"{baseName} ({suffix++}){extension}");
        }

        await using var source = await photo.OpenReadAsync();
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination);

        var importedFileName = Path.GetFileName(destinationPath);
        _targetFileNames[importedFileName] = Path.GetFileNameWithoutExtension(importedFileName);
        return importedFileName;
    }

    public async Task SaveSettingsAsync(string selectedPictureFileName, IReadOnlyDictionary<string, string> targetFileNames)
    {
        if (!GetPictureFileNames().Contains(selectedPictureFileName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected picture is no longer available.");
        }

        var safeTargetNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pictureFileName, targetFileName) in targetFileNames)
        {
            if (!GetPictureFileNames().Contains(pictureFileName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            safeTargetNames[pictureFileName] = GetSafeFileName(targetFileName);
        }

        var settings = new PictureLibrarySettings
        {
            SelectedPictureFileName = selectedPictureFileName,
            TargetFileNames = safeTargetNames
        };
        var settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_settingsFilePath, settingsJson);

        _targetFileNames.Clear();
        foreach (var pair in safeTargetNames)
        {
            _targetFileNames[pair.Key] = pair.Value;
        }

        SelectedPictureFileName = selectedPictureFileName;
    }

    private static string GetSafeFileName(string fileName)
    {
        var safeFileName = fileName.Trim();
        if (string.IsNullOrWhiteSpace(safeFileName) ||
            safeFileName is "." or ".." ||
            safeFileName.Contains('/') ||
            safeFileName.Contains('\\') ||
            safeFileName != Path.GetFileName(safeFileName) ||
            safeFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("File names must be a single valid name without a folder path.");
        }

        return safeFileName;
    }

    private static bool IsPictureFile(string path)
    {
        return IsSupportedPictureExtension(Path.GetExtension(path));
    }

    private static bool IsSupportedPictureExtension(string extension)
    {
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PictureLibrarySettings
    {
        public string SelectedPictureFileName { get; set; } = "picture.png";
        public Dictionary<string, string> TargetFileNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public PictureLibrarySettings()
        {
        }
    }
}
