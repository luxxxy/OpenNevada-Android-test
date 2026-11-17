namespace OpenNV.Runtime.Content;

internal enum NativeGame
{
    Fallout1,
    Fallout2,
    Fallout3,
    FalloutNewVegas,
}

internal sealed record NativeGameInstallation(NativeGame Game, string InstallRoot, string ContentRoot)
{
    private const string FalloutNewVegasMasterName = "FalloutNV" + ".esm";

    internal bool IsTaleOfTwoWastelands => ContainsFile(ContentRoot, "TaleOfTwoWastelands.esm");

    internal static NativeGameInstallation Detect(string selectedRoot)
    {
        if (string.IsNullOrWhiteSpace(selectedRoot))
            throw new ArgumentException("A game installation root is required.", nameof(selectedRoot));

        var root = Path.GetFullPath(selectedRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Selected game folder does not exist: {root}");

        var dataRoot = FindDirectory(root, "Data");
        var contentRoot = dataRoot ?? root;
        var game = DetectGame(root, contentRoot);
        return new NativeGameInstallation(game, root, contentRoot);
    }

    private static NativeGame DetectGame(string installRoot, string contentRoot)
    {
        if (ContainsFile(installRoot, "patch000.dat") &&
            ContainsFile(installRoot, "critter.dat") &&
            ContainsFile(installRoot, "master.dat"))
            return NativeGame.Fallout2;
        if (ContainsFile(installRoot, "critter.dat") && ContainsFile(installRoot, "master.dat"))
            return NativeGame.Fallout1;
        // TTW uses the New Vegas engine even though its output also contains
        // Fallout3.esm. Do not route it through standalone Fallout 3 behavior.
        if (ContainsFile(contentRoot, FalloutNewVegasMasterName))
            return NativeGame.FalloutNewVegas;
        if (ContainsFile(contentRoot, "Fallout3.esm"))
            return NativeGame.Fallout3;
        var rootEntries = DescribeEntries(installRoot);
        var dataEntries = string.IsNullOrWhiteSpace(contentRoot) || contentRoot.Equals(installRoot, StringComparison.OrdinalIgnoreCase)
            ? "Data folder not found"
            : DescribeEntries(contentRoot);
        throw new InvalidDataException(
            $"Open Nevada could not recognize the selected game folder: {installRoot}. " +
            $"Expected FalloutNV.esm inside Data. Root entries: {rootEntries}. " +
            $"Data entries: {dataEntries}. Check Android file access and select Documents/fnv again.");
    }

    private static string? FindDirectory(string root, string name) =>
        Directory.EnumerateDirectories(root)
            .SingleOrDefault(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string DescribeEntries(string root)
    {
        try
        {
            return string.Join(", ", Directory.EnumerateFileSystemEntries(root)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(24));
        }
        catch (Exception exception)
        {
            return $"<unavailable: {exception.GetType().Name}>";
        }
    }

    private static bool ContainsFile(string root, string name) =>
        Directory.EnumerateFiles(root)
            .Any(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));
}
