using System.Text.Json;

namespace NafReDaw;

public static class Project
{
    public static DawProject? LoadProject(string? filename = null)
    {
        var currentDirectory = Directory.GetCurrentDirectory();

        if (filename is null)
        {
            var folderName = new DirectoryInfo(currentDirectory).Name;
            filename = folderName + App.ProjectFileExtension;
        }

        var path = Path.IsPathRooted(filename)
            ? filename
            : Path.Combine(currentDirectory, filename);

        if (!Path.Exists(path))
        {
            App.Output($"File not found '{path}'.");
            return null;
        }

        var json = File.ReadAllText(path);
        var loadedProject = JsonSerializer.Deserialize<DawProject>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (loadedProject is null)
        {
            App.Output($"Failed to parse project JSON from '{path}'.");
            return null;
        }

        App.ChangesMade = false;
        loadedProject.Arrangement ??= new Arrangement();
        ArrangeSystem.EnsureInitialized(loadedProject.Arrangement);
        App.Output("Project loaded.");

        return loadedProject;
    }

    public static void SaveProject(string? filename = null)
    {
        var currentDirectory = Directory.GetCurrentDirectory();

        if (filename is null)
        {
            var folderName = new DirectoryInfo(currentDirectory).Name;
            filename = folderName + App.ProjectFileExtension;
        }

        var path = Path.IsPathRooted(filename)
            ? filename
            : Path.Combine(currentDirectory, filename);

        var json = JsonSerializer.Serialize(
            App.Project,
            new JsonSerializerOptions { WriteIndented = true }
        );

        File.WriteAllText(path, json);
        App.ChangesMade = false;
        App.Output($"Project saved to '{path}'.");
    }

    public static void Dir(string? filter = null)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var directoryInfo = new DirectoryInfo(currentDirectory);

        FileSystemInfo[] entries;
        try
        {
            entries = directoryInfo.GetFileSystemInfos();
        }
        catch (Exception ex)
        {
            App.Output($"Failed to list directory '{currentDirectory}': {ex.Message}");
            return;
        }

        var printedAny = false;
        foreach (var entry in entries.OrderBy(e => e is FileInfo).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(filter) &&
                entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) is false)
            {
                continue;
            }

            var type = entry is DirectoryInfo ? "<DIR>" : "     ";
            App.Output($"{type} {entry.Name}");
            printedAny = true;
        }

        if (!printedAny)
        {
            App.Output("<EMPTY>");
        }
    }
}

