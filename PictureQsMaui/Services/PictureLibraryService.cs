using System.IO.Compression;
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

        PictureLibrarySettings? settings = null;
        if (File.Exists(_settingsFilePath))
        {
            var settingsJson = await File.ReadAllTextAsync(_settingsFilePath);
            settings = JsonSerializer.Deserialize<PictureLibrarySettings>(settingsJson);
            if (settings is not null)
            {
                foreach (var pair in settings.TargetFileNames)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    {
                        _targetFileNames[pair.Key] = pair.Value;
                    }
                }

            }
        }

        if (settings?.BundledPicturesInitialized != true)
        {
            var defaultPicturePath = Path.Combine(_folderPath, "picture.png");
            if (!File.Exists(defaultPicturePath) && !GetPictureFileNames().Any())
            {
                await CopyBundledPictureAsync("picture.png", defaultPicturePath);
            }

            var blankedPicturePath = Path.Combine(_folderPath, "picture-Blanked.png");
            if (!File.Exists(blankedPicturePath))
            {
                await CopyBundledPictureAsync("picture-Blanked.png", blankedPicturePath);
            }
        }

        if (settings is not null &&
            GetPictureFileNames().Contains(settings.SelectedPictureFileName, StringComparer.OrdinalIgnoreCase))
        {
            SelectedPictureFileName = settings.SelectedPictureFileName;
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
        if (settings?.BundledPicturesInitialized != true)
        {
            _targetFileNames["picture-Blanked.png"] = GetTargetsFileName("picture.png");
        }
        else
        {
            _targetFileNames.TryAdd("picture-Blanked.png", GetTargetsFileName("picture.png"));
        }

        foreach (var fileName in GetPictureFileNames())
        {
            _targetFileNames.TryAdd(fileName, Path.GetFileNameWithoutExtension(fileName));
        }

        if (settings?.BundledPicturesInitialized != true)
        {
            var targetFileNames = GetPictureFileNames().ToDictionary(
                fileName => fileName,
                GetTargetsFileName,
                StringComparer.OrdinalIgnoreCase);
            await SaveSettingsAsync(SelectedPictureFileName, targetFileNames);
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

    public IReadOnlyList<string> GetTargetsJsonFileNames()
    {
        return Directory.EnumerateFiles(_folderPath)
            .Where(path =>
                Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(path).Equals(SettingsFileName, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => fileName!)
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

    public async Task<string> CreateBackupArchiveAsync()
    {
        var filesToBackUp = Directory.EnumerateFiles(_folderPath)
            .Where(path =>
                IsPictureFile(path) ||
                Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (filesToBackUp.Count == 0)
        {
            throw new InvalidOperationException("There are no picture or JSON files to back up.");
        }

        var archivePath = Path.Combine(
            FileSystem.CacheDirectory,
            $"PictureQs-backup-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");

        try
        {
            await using var archiveStream = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true);

            foreach (var filePath in filesToBackUp)
            {
                var entry = archive.CreateEntry(Path.GetFileName(filePath), CompressionLevel.Optimal);
                await using var source = File.OpenRead(filePath);
                await using var destination = entry.Open();
                await source.CopyToAsync(destination);
            }
        }
        catch
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            throw;
        }

        return archivePath;
    }

    public async Task RestoreBackupArchiveAsync(Stream archiveSource)
    {
        var temporaryFolder = Path.Combine(FileSystem.CacheDirectory, $"restore-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(FileSystem.CacheDirectory, $"restore-{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(temporaryFolder);

        try
        {
            await using (var archiveFile = File.Create(archivePath))
            {
                await archiveSource.CopyToAsync(archiveFile);
            }

            var restoredFiles = new List<string>();
            var restoredFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    var fileName = entry.FullName;
                    if (string.IsNullOrWhiteSpace(fileName) ||
                        fileName.Contains('/') ||
                        fileName.Contains('\\') ||
                        !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("The backup contains a file path that is not allowed.");
                    }

                    fileName = GetSafeFileName(fileName);
                    if (!IsPictureFile(fileName) &&
                        !Path.GetExtension(fileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"The backup contains an unsupported file: '{fileName}'.");
                    }

                    if (!restoredFileNames.Add(fileName))
                    {
                        throw new InvalidDataException($"The backup contains more than one file named '{fileName}'.");
                    }

                    var stagedPath = Path.Combine(temporaryFolder, fileName);
                    await using var source = entry.Open();
                    await using var destination = new FileStream(
                        stagedPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None);
                    await source.CopyToAsync(destination);
                    restoredFiles.Add(stagedPath);
                }
            }

            var pictureFileNames = restoredFileNames.Where(IsPictureFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (pictureFileNames.Count == 0)
            {
                throw new InvalidDataException("The backup does not contain any supported pictures.");
            }

            var settingsPath = Path.Combine(temporaryFolder, SettingsFileName);
            if (File.Exists(settingsPath))
            {
                var settingsJson = await File.ReadAllTextAsync(settingsPath);
                var settings = JsonSerializer.Deserialize<PictureLibrarySettings>(settingsJson);
                if (settings?.TargetFileNames is null ||
                    string.IsNullOrWhiteSpace(settings.SelectedPictureFileName) ||
                    !pictureFileNames.Contains(GetSafeFileName(settings.SelectedPictureFileName)))
                {
                    throw new InvalidDataException("The backup contains invalid picture library settings.");
                }

                foreach (var (pictureFileName, targetFileName) in settings.TargetFileNames)
                {
                    if (string.IsNullOrWhiteSpace(pictureFileName) ||
                        string.IsNullOrWhiteSpace(targetFileName) ||
                        !pictureFileNames.Contains(GetSafeFileName(pictureFileName)))
                    {
                        throw new InvalidDataException("The backup contains invalid picture library settings.");
                    }

                    GetSafeFileName(targetFileName);
                }
            }

            Directory.CreateDirectory(_folderPath);
            foreach (var stagedPath in restoredFiles)
            {
                File.Copy(stagedPath, Path.Combine(_folderPath, Path.GetFileName(stagedPath)), overwrite: true);
            }

            _targetFileNames.Clear();
            SelectedPictureFileName = "picture.png";
            await InitializeAsync();

            var availablePictureNames = GetPictureFileNames();
            if (!availablePictureNames.Contains(SelectedPictureFileName, StringComparer.OrdinalIgnoreCase))
            {
                var targetFileNames = availablePictureNames.ToDictionary(
                    fileName => fileName,
                    GetTargetsFileName,
                    StringComparer.OrdinalIgnoreCase);
                await SaveSettingsAsync(availablePictureNames[0], targetFileNames);
            }
        }
        finally
        {
            if (Directory.Exists(temporaryFolder))
            {
                Directory.Delete(temporaryFolder, recursive: true);
            }

            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
    }

    public async Task<string> ImportPictureAsync(FileResult photo, string pictureName, string targetsFileName)
    {
        var fileName = Path.GetFileName(photo.FileName);
        var extension = Path.GetExtension(fileName);
        if (!IsSupportedPictureExtension(extension))
        {
            throw new InvalidDataException($"The selected file type '{extension}' is not supported.");
        }

        var safePictureName = GetSafeFileName(pictureName);
        var safeTargetsFileName = GetSafeFileName(targetsFileName);
        var importedFileName = $"{safePictureName}{extension}";
        if (GetPictureFileNames().Contains(importedFileName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"A picture named '{safePictureName}' already exists.");
        }

        var destinationPath = Path.Combine(_folderPath, importedFileName);
        await using var source = await photo.OpenReadAsync();
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write);
        await source.CopyToAsync(destination);

        _targetFileNames[importedFileName] = safeTargetsFileName;
        return importedFileName;
    }

    public async Task<string> RenamePictureAsync(
        string pictureFileName,
        string pictureName,
        string targetsFileName,
        string selectedPictureFileName,
        IReadOnlyDictionary<string, string> targetFileNames,
        bool reuseExistingTargetsFile = false)
    {
        var safePictureFileName = GetSafeFileName(pictureFileName);
        var oldPicturePath = GetPicturePath(safePictureFileName);
        if (!File.Exists(oldPicturePath))
        {
            throw new InvalidOperationException("The picture is no longer available.");
        }

        var safePictureName = GetSafeFileName(pictureName);
        var safeTargetsFileName = GetSafeFileName(targetsFileName);
        var renamedPictureFileName = $"{safePictureName}{Path.GetExtension(safePictureFileName)}";
        var renamedPicturePath = GetPicturePath(renamedPictureFileName);
        if (!string.Equals(safePictureFileName, renamedPictureFileName, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(renamedPicturePath))
        {
            throw new IOException($"A picture named '{safePictureName}' already exists.");
        }

        var oldTargetsFileName = GetTargetsFileName(safePictureFileName);
        var oldTargetsPath = GetTargetsPath(safePictureFileName);
        var renamedTargetsPath = Path.Combine(_folderPath, $"{safeTargetsFileName}.json");
        var targetsFileNameChanged = !string.Equals(oldTargetsPath, renamedTargetsPath, StringComparison.OrdinalIgnoreCase);
        var sharedTargetsFile = GetPictureFileNames().Any(fileName =>
            !string.Equals(fileName, safePictureFileName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                targetFileNames.TryGetValue(fileName, out var targetName) ? targetName.Trim() : GetTargetsFileName(fileName),
                oldTargetsFileName,
                StringComparison.OrdinalIgnoreCase));
        var movedTargetsFile = false;
        var copiedTargetsFile = false;
        var renamedPicture = false;

        var existingTargetsFile = targetsFileNameChanged && File.Exists(renamedTargetsPath);
        if (existingTargetsFile && !reuseExistingTargetsFile)
        {
            throw new IOException($"A JSON file named '{safeTargetsFileName}.json' already exists.");
        }

        try
        {
            if (targetsFileNameChanged && File.Exists(oldTargetsPath) && !existingTargetsFile)
            {
                if (sharedTargetsFile)
                {
                    File.Copy(oldTargetsPath, renamedTargetsPath);
                    copiedTargetsFile = true;
                }
                else
                {
                    File.Move(oldTargetsPath, renamedTargetsPath);
                    movedTargetsFile = true;
                }
            }

            if (!string.Equals(safePictureFileName, renamedPictureFileName, StringComparison.Ordinal))
            {
                File.Move(oldPicturePath, renamedPicturePath);
                renamedPicture = true;
            }

            var updatedTargetFileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fileName, targetName) in targetFileNames)
            {
                if (!GetPictureFileNames().Contains(fileName, StringComparer.OrdinalIgnoreCase) &&
                    !string.Equals(fileName, safePictureFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var updatedFileName = string.Equals(fileName, safePictureFileName, StringComparison.OrdinalIgnoreCase)
                    ? renamedPictureFileName
                    : fileName;
                updatedTargetFileNames[updatedFileName] = string.Equals(fileName, safePictureFileName, StringComparison.OrdinalIgnoreCase)
                    ? safeTargetsFileName
                    : GetSafeFileName(targetName);
            }

            await SaveSettingsAsync(
                selectedPictureFileName,
                updatedTargetFileNames);

            if (existingTargetsFile &&
                File.Exists(oldTargetsPath) &&
                !sharedTargetsFile &&
                !string.Equals(oldTargetsPath, renamedTargetsPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(oldTargetsPath);
            }

            return renamedPictureFileName;
        }
        catch
        {
            if (renamedPicture && File.Exists(renamedPicturePath))
            {
                File.Move(renamedPicturePath, oldPicturePath);
            }

            if (movedTargetsFile && File.Exists(renamedTargetsPath))
            {
                File.Move(renamedTargetsPath, oldTargetsPath);
            }
            else if (copiedTargetsFile && File.Exists(renamedTargetsPath))
            {
                File.Delete(renamedTargetsPath);
            }

            throw;
        }
    }

    public async Task DeletePictureAsync(
        string pictureFileName,
        string selectedPictureFileName,
        IReadOnlyDictionary<string, string> targetFileNames)
    {
        var safePictureFileName = GetSafeFileName(pictureFileName);
        var picturePath = GetPicturePath(safePictureFileName);
        var pictureFileNames = GetPictureFileNames();
        if (!pictureFileNames.Contains(safePictureFileName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The picture is no longer available.");
        }

        var remainingPictureFileNames = pictureFileNames
            .Where(fileName => !string.Equals(fileName, safePictureFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (remainingPictureFileNames.Count == 0)
        {
            throw new InvalidOperationException("At least one picture must remain in the library.");
        }

        var safeSelectedPictureFileName = remainingPictureFileNames.Contains(
            selectedPictureFileName,
            StringComparer.OrdinalIgnoreCase)
                ? selectedPictureFileName
                : remainingPictureFileNames[0];
        var deletedTargetsPath = GetTargetsPath(safePictureFileName);
        var remainingTargetFileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var remainingPictureFileName in remainingPictureFileNames)
        {
            var targetFileName = targetFileNames.TryGetValue(remainingPictureFileName, out var suppliedTargetFileName)
                ? suppliedTargetFileName
                : GetTargetsFileName(remainingPictureFileName);
            remainingTargetFileNames[remainingPictureFileName] = GetSafeFileName(targetFileName);
        }

        await SaveSettingsAsync(safeSelectedPictureFileName, remainingTargetFileNames);
        File.Delete(picturePath);

        var targetsFileIsStillAssociated = remainingTargetFileNames.Values.Any(targetFileName =>
            string.Equals(
                Path.GetFullPath(Path.Combine(_folderPath, $"{GetSafeFileName(targetFileName)}.json")),
                Path.GetFullPath(deletedTargetsPath),
                StringComparison.OrdinalIgnoreCase));
        if (!targetsFileIsStillAssociated)
        {
            File.Delete(deletedTargetsPath);
        }
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
            TargetFileNames = safeTargetNames,
            BundledPicturesInitialized = true
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

    private static async Task CopyBundledPictureAsync(string assetFileName, string destinationPath)
    {
        await using var source = await FileSystem.OpenAppPackageFileAsync(assetFileName);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination);
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
        public bool BundledPicturesInitialized { get; set; }

        public PictureLibrarySettings()
        {
        }
    }
}
