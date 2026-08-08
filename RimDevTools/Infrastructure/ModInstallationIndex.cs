using System.Xml.Linq;

namespace RimDevTools;

public sealed record ModInstallation(string PackageId, string Directory, string Source);
public sealed record ModIndexDiagnostic(string Path, string Message);

public sealed class ModInstallationIndex
{
    private readonly Dictionary<string, List<ModInstallation>> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ModIndexDiagnostic> _diagnostics = [];

    public IReadOnlyList<ModIndexDiagnostic> Diagnostics => _diagnostics;
    public IReadOnlyCollection<string> PackageIds => _items.Keys;

    public static ModInstallationIndex Build(string? rimWorldDirectory, IEnumerable<string> workshopPaths, string? projectRoot = null)
    {
        var index = new ModInstallationIndex();
        if (!string.IsNullOrWhiteSpace(projectRoot)) index.AddDirectory(projectRoot, "project");
        if (!string.IsNullOrWhiteSpace(rimWorldDirectory))
        {
            index.ScanRoot(Path.Combine(rimWorldDirectory, "Data"), "official");
            index.ScanRoot(Path.Combine(rimWorldDirectory, "Mods"), "local");
        }
        foreach (var path in workshopPaths.Distinct(StringComparer.OrdinalIgnoreCase)) index.ScanRoot(path, "workshop");
        return index;
    }

    public ModInstallation? Find(string packageId)
        => _items.TryGetValue(packageId, out var values) ? values.FirstOrDefault() : null;

    public IReadOnlyList<ModInstallation> FindAll(string packageId)
        => _items.TryGetValue(packageId, out var values) ? values : [];

    private void ScanRoot(string root, string source)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root)) AddDirectory(directory, source);
        }
        catch (Exception ex)
        {
            _diagnostics.Add(new ModIndexDiagnostic(root, ex.Message));
        }
    }

    private void AddDirectory(string directory, string source)
    {
        var aboutPath = Path.Combine(directory, "About", "About.xml");
        if (!File.Exists(aboutPath)) return;
        try
        {
            var packageId = XDocument.Load(aboutPath).Root?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("packageId", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
            if (string.IsNullOrWhiteSpace(packageId)) throw new InvalidDataException("About.xml 缺少 packageId");
            if (!_items.TryGetValue(packageId, out var values)) _items[packageId] = values = [];
            values.Add(new ModInstallation(packageId, Path.GetFullPath(directory), source));
        }
        catch (Exception ex)
        {
            _diagnostics.Add(new ModIndexDiagnostic(aboutPath, ex.Message));
        }
    }
}
