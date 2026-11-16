using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record GodotSaveMigrationResult(
    int ImportedSaveSlots,
    int ImportedScriptFiles,
    int SkippedScriptConflicts);

internal sealed record GodotSaveMigrationExportResult(
    int ExportedSaveFiles,
    int ExportedScriptFiles);

internal static class GodotSaveMigrationBundle
{
    private const string BundleSchema = "opennv-save-migration-bundle/v1";
    private const long MaximumUncompressedBytes = 256L * 1024 * 1024;
    private const int MaximumEntries = 512;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public sealed record ManifestFile(string Path, long Length, string Sha256);
    public sealed record Manifest(
        string Schema,
        string CampaignId,
        DateTime CreatedUtc,
        IReadOnlyList<ManifestFile> Files);

    internal static GodotSaveMigrationExportResult Export(
        string campaignId,
        string canonicalSavePath,
        string bundlePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSavePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        var savePath = Path.GetFullPath(canonicalSavePath);
        var parent = Path.GetDirectoryName(savePath) ??
            throw new InvalidOperationException("The save path has no parent directory.");
        var files = new List<(string ArchivePath, string SourcePath)>();
        var saveCount = 0;

        if (File.Exists(savePath))
        {
            ValidateSaveFile(savePath);
            files.Add(("saves/current.json", savePath));
            saveCount++;
        }

        var slotDirectory = savePath + RuntimeSaveSlotCatalog.SlotDirectorySuffix;
        if (Directory.Exists(slotDirectory))
        {
            foreach (var slot in Directory.EnumerateFiles(slotDirectory, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                var id = Path.GetFileNameWithoutExtension(slot);
                if (!Guid.TryParseExact(id, "N", out _))
                    continue;
                ValidateSaveFile(slot);
                files.Add(($"saves/slots/{id}.json", slot));
                saveCount++;
            }
        }

        var scriptDirectory = Path.Combine(parent, "script-config");
        var scriptCount = 0;
        if (Directory.Exists(scriptDirectory))
        {
            foreach (var file in EnumerateSafeFiles(scriptDirectory))
            {
                var relative = Path.GetRelativePath(scriptDirectory, file).Replace('\\', '/');
                ValidateRelativePath(relative);
                files.Add(($"script-config/{relative}", file));
                scriptCount++;
            }
        }

        if (saveCount == 0)
            throw new FileNotFoundException("There are no New Vegas saves to export yet.");
        if (files.Count > MaximumEntries)
            throw new InvalidDataException("The backup contains more than 512 files.");

        long totalBytes = 0;
        var manifestFiles = new List<ManifestFile>(files.Count);
        foreach (var (archivePath, sourcePath) in files)
        {
            var info = new FileInfo(sourcePath);
            totalBytes = checked(totalBytes + info.Length);
            if (totalBytes > MaximumUncompressedBytes)
                throw new InvalidDataException("The save backup is larger than the supported 256 MB limit.");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant();
            manifestFiles.Add(new(archivePath, info.Length, hash));
        }

        var manifest = new Manifest(BundleSchema, campaignId.ToLowerInvariant(), DateTime.UtcNow, manifestFiles);
        var target = Path.GetFullPath(bundlePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(manifestEntry.Open()))
                    writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));

                foreach (var (archivePath, sourcePath) in files)
                {
                    var entry = archive.CreateEntry(archivePath, CompressionLevel.Optimal);
                    using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var destination = entry.Open();
                    source.CopyTo(destination);
                }
            }
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }

        return new(saveCount, scriptCount);
    }

    internal static GodotSaveMigrationResult Import(
        string campaignId,
        string canonicalSavePath,
        string bundlePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSavePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        var savePath = Path.GetFullPath(canonicalSavePath);
        var parent = Path.GetDirectoryName(savePath) ??
            throw new InvalidOperationException("The save path has no parent directory.");
        var bundle = Path.GetFullPath(bundlePath);
        using var archive = ZipFile.OpenRead(bundle);
        if (archive.Entries.Count is < 2 or > MaximumEntries + 1)
            throw new InvalidDataException("The backup has an invalid number of entries.");

        var manifestEntry = archive.GetEntry("manifest.json") ??
            throw new InvalidDataException("The backup has no manifest.");
        if (manifestEntry.Length > 1024 * 1024)
            throw new InvalidDataException("The backup manifest is too large.");
        Manifest manifest;
        using (var reader = new StreamReader(manifestEntry.Open()))
            manifest = JsonSerializer.Deserialize<Manifest>(reader.ReadToEnd(), JsonOptions) ??
                throw new InvalidDataException("The backup manifest is empty.");

        if (manifest.Schema != BundleSchema ||
            !manifest.CampaignId.Equals(campaignId, StringComparison.OrdinalIgnoreCase) ||
            manifest.Files is null || manifest.Files.Count == 0 || manifest.Files.Count > MaximumEntries)
            throw new InvalidDataException("The backup belongs to another campaign or has an unsupported format.");

        var archiveFiles = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName == "manifest.json")
                continue;
            ValidateArchivePath(entry.FullName);
            if (!archiveFiles.TryAdd(entry.FullName, entry))
                throw new InvalidDataException("The backup contains duplicate file paths.");
            totalBytes = checked(totalBytes + entry.Length);
            if (entry.Length < 0 || totalBytes > MaximumUncompressedBytes)
                throw new InvalidDataException("The backup exceeds the supported 256 MB limit.");
        }

        if (archiveFiles.Count != manifest.Files.Count)
            throw new InvalidDataException("The backup manifest does not match its contents.");

        var validated = new List<(string Path, byte[] Bytes)>(manifest.Files.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var saveCount = 0;
        foreach (var file in manifest.Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Path) || file.Length < 0 ||
                string.IsNullOrWhiteSpace(file.Sha256) || file.Sha256.Length != 64 || !seen.Add(file.Path) ||
                !archiveFiles.TryGetValue(file.Path, out var entry) || entry.Length != file.Length)
                throw new InvalidDataException("The backup manifest contains an invalid file entry.");

            var bytes = ReadEntry(entry);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Backup integrity check failed for {file.Path}.");

            if (file.Path is "saves/current.json" || file.Path.StartsWith("saves/slots/", StringComparison.Ordinal))
            {
                ValidateSaveBytes(bytes);
                saveCount++;
            }
            else if (!file.Path.StartsWith("script-config/", StringComparison.Ordinal))
                throw new InvalidDataException("The backup contains an unsupported file type.");

            validated.Add((file.Path, bytes));
        }

        if (saveCount == 0)
            throw new InvalidDataException("The backup contains no campaign saves.");

        var staging = Path.Combine(parent, ".opennv-save-import-" + Guid.NewGuid().ToString("N"));
        var slotDirectory = savePath + RuntimeSaveSlotCatalog.SlotDirectorySuffix;
        var scriptDirectory = Path.Combine(parent, "script-config");
        var created = new List<string>();
        var importedSlots = 0;
        var importedScriptFiles = 0;
        var skippedScriptConflicts = 0;
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var (relative, bytes) in validated)
            {
                var stagedPath = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                File.WriteAllBytes(stagedPath, bytes);
            }

            Directory.CreateDirectory(slotDirectory);
            foreach (var (relative, _) in validated.Where(value => value.Path.StartsWith("saves/", StringComparison.Ordinal)))
            {
                var stagedPath = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                var destination = Path.Combine(slotDirectory, Guid.NewGuid().ToString("N") + ".json");
                File.Move(stagedPath, destination);
                created.Add(destination);
                importedSlots++;
            }

            foreach (var (relative, _) in validated.Where(value => value.Path.StartsWith("script-config/", StringComparison.Ordinal)))
            {
                var scriptRelative = relative["script-config/".Length..];
                var destination = Path.Combine(scriptDirectory, scriptRelative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(destination))
                {
                    skippedScriptConflicts++;
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var stagedPath = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                File.Move(stagedPath, destination);
                created.Add(destination);
                importedScriptFiles++;
            }
        }
        catch
        {
            foreach (var path in created)
                if (File.Exists(path))
                    File.Delete(path);
            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
        }

        return new(importedSlots, importedScriptFiles, skippedScriptConflicts);
    }

    internal static GodotSaveMigrationResult ImportLegacySaveFile(
        string campaignId,
        string canonicalSavePath,
        string legacySavePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSavePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacySavePath);
        if (!campaignId.Equals("newvegas", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Standalone legacy save import is only supported for New Vegas.");
        var bytes = File.ReadAllBytes(Path.GetFullPath(legacySavePath));
        ValidateSaveBytes(bytes);
        var savePath = Path.GetFullPath(canonicalSavePath);
        var slotDirectory = savePath + RuntimeSaveSlotCatalog.SlotDirectorySuffix;
        Directory.CreateDirectory(slotDirectory);
        var destination = Path.Combine(slotDirectory, Guid.NewGuid().ToString("N") + ".json");
        var temporary = destination + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        return new(1, 0, 0);
    }

    private static IEnumerable<string> EnumerateSafeFiles(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Script configuration contains a linked path and cannot be exported safely.");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var nested in EnumerateSafeFiles(path))
                    yield return nested;
            }
            else
                yield return path;
        }
    }

    private static void ValidateSaveFile(string path) =>
        ValidateSaveBytes(File.ReadAllBytes(path));

    private static void ValidateSaveBytes(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("Schema", out var schema) ||
            schema.ValueKind != JsonValueKind.String ||
            schema.GetString() is not { } schemaName ||
            !schemaName.StartsWith("opennv-native-fnv-campaign-save/", StringComparison.Ordinal) ||
            !root.TryGetProperty("SaveCompatibilityId", out var identity) ||
            identity.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(identity.GetString()))
            throw new InvalidDataException("A campaign save in the backup is invalid or unsupported.");
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var source = entry.Open();
        using var destination = new MemoryStream(checked((int)Math.Min(entry.Length, int.MaxValue)));
        source.CopyTo(destination);
        if (destination.Length != entry.Length)
            throw new InvalidDataException($"Backup entry length differs for {entry.FullName}.");
        return destination.ToArray();
    }

    private static void ValidateArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.StartsWith('/') ||
            Path.IsPathRooted(path) || path.Contains(':'))
            throw new InvalidDataException("The backup contains an unsafe file path.");
        ValidateRelativePath(path);
        if (!(path is "saves/current.json" ||
              path.StartsWith("saves/slots/", StringComparison.Ordinal) ||
              path.StartsWith("script-config/", StringComparison.Ordinal)))
            throw new InvalidDataException("The backup contains an unsupported file path.");
        if (path.StartsWith("saves/slots/", StringComparison.Ordinal) &&
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _))
            throw new InvalidDataException("The backup contains an invalid save-slot identity.");
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.Any(char.IsControl)))
            throw new InvalidDataException("The backup contains an invalid relative path.");
    }
}
