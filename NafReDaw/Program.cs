
using NafMidi;
using System.Diagnostics;
using System.Text.Json;

namespace NafReDaw;
internal class Program
{
    private const string ProjectFileExtension = ".nafdaw";
    private const string DebugProjcetFolder = "C:\\Users\\thora\\Google Drive\\Music\\NafDaw\\Debug";

    static void Main(string[] args)
    {
        if (Debugger.IsAttached && Directory.Exists(DebugProjcetFolder))
        {
            Directory.SetCurrentDirectory(DebugProjcetFolder);
        }

        LaunchpadDevice launchpad = null!;

        var project = new DawProject();

        var defaultProjectPath = new DirectoryInfo(Directory.GetCurrentDirectory()).Name + ProjectFileExtension;
        if (File.Exists(defaultProjectPath))
        {
            project = LoadProject(defaultProjectPath) ?? project;
            launchpad = ApplyProject(project, launchpad);
        }
        else
        {
            launchpad = CreateLaunchPadDevice();
        }

        Console.WriteLine("Ready!");

        var quit = false;
        try
        {
            while (!quit)
            {
                var command = Console.ReadLine();
                var parameters = command?.Split(" ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray() ?? [];
                if (parameters.Length > 0)
                {
                    command = parameters[0];
                    parameters = parameters.Skip(1).ToArray();
                }

                switch (command)
                {
                    case "q":
                    case "quit":
                        {
                            quit = true;
                            break;
                        }
                    case "d" when parameters.Length == 0:
                    case "devices":
                        {
                            foreach (var d in LaunchpadDevice.ListInputDevices())
                                Console.WriteLine($"{d.Index}: {d.Name}");
                            break;
                        }
                    case "d" when parameters.Length != 0:
                    case "device" when parameters.Length != 0:
                        {
                            if (!Int32.TryParse(parameters[0], out var index))
                            {
                                Console.WriteLine($"Illegal index {parameters[0]}");
                                break;
                            }
                            var inputDeviceIndex = index;
                            var outputDeviceIndex = index;
                            if (parameters.Length > 1)
                            {
                                if (!Int32.TryParse(parameters[1], out index))
                                {
                                    Console.WriteLine($"Illegal index {parameters[1]}");
                                    break;
                                }
                                outputDeviceIndex = index;
                            }

                            launchpad.Stop();
                            launchpad.Dispose();
                            launchpad = CreateLaunchPadDevice(inputDeviceIndex: inputDeviceIndex, outputDeviceIndex: outputDeviceIndex);
                            project.MidiInputDeviceIndex = inputDeviceIndex;
                            project.MidiOutputDeviceIndex = outputDeviceIndex;
                            project.ChangesMade = true;
                            break;
                        }
                    case "l":
                    case "load":
                        {
                            project = LoadProject(parameters.Length > 0 ? parameters[0] : null) ?? project;
                            launchpad = ApplyProject(project, launchpad);
                            break;
                        }
                    case "s":
                    case "save":
                        {
                            SaveProject(project, parameters.Length > 0 ? parameters[0] : null);
                            break;
                        }
                    case "dir":
                    case "ls":
                        {
                            Dir(parameters.Length > 0 ? parameters[0] : null);
                            break;
                        }
                }
            }
        }
        finally
        {
            launchpad.Stop();
            launchpad.Dispose();
        }

        Console.WriteLine("Goodbye!");
    }

    static LaunchpadDevice CreateLaunchPadDevice(int inputDeviceIndex = 0, int outputDeviceIndex = 0)
    {
        var launchpad = new LaunchpadDevice();

        launchpad.PadPressed += (_, e) =>
            Console.WriteLine($"Pad {e.Row},{e.Column} note 0x{e.NoteNumber:X2}");
        launchpad.SideButtonPressed += (_, e) =>
            Console.WriteLine($"CC {e.ControllerNumber} = {e.Value}");

        launchpad.Start(inputDeviceIndex: inputDeviceIndex, outputDeviceIndex: outputDeviceIndex);

        Console.WriteLine($"Opening MIDI devices in {inputDeviceIndex} out {outputDeviceIndex}...");

        return launchpad;
    }

    static LaunchpadDevice ApplyProject(DawProject project, LaunchpadDevice? currentDevice)
    {
        currentDevice?.Stop();
        currentDevice?.Dispose();

        return CreateLaunchPadDevice(
            inputDeviceIndex: project.MidiInputDeviceIndex,
            outputDeviceIndex: project.MidiOutputDeviceIndex
        );
    }

    static DawProject? LoadProject(string? filename = null)
    {
        var currentDirectory = Directory.GetCurrentDirectory();

        if (filename is null)
        {
            var folderName = new DirectoryInfo(currentDirectory).Name;
            filename = folderName + ProjectFileExtension;
        }

        var path = Path.IsPathRooted(filename)
            ? filename
            : Path.Combine(currentDirectory, filename);

        if (!Path.Exists(path))
        {
            Console.WriteLine($"File not found '{path}'.");
            return null;
        }

        var json = File.ReadAllText(path);
        var project = JsonSerializer.Deserialize<DawProject>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (project is null)
        {
            Console.WriteLine($"Failed to parse project JSON from '{path}'.");
            return null;
        }

        project.ChangesMade = false;
        Console.WriteLine("Project loaded.");

        return project;
    }

    static void SaveProject(DawProject project, string? filename = null)
    {
        var currentDirectory = Directory.GetCurrentDirectory();

        if (filename is null)
        {
            var folderName = new DirectoryInfo(currentDirectory).Name;
            filename = folderName + ProjectFileExtension;
        }

        var path = Path.IsPathRooted(filename)
            ? filename
            : Path.Combine(currentDirectory, filename);

        var json = JsonSerializer.Serialize(
            project,
            new JsonSerializerOptions { WriteIndented = true }
        );

        File.WriteAllText(path, json);
        project.ChangesMade = false;
        Console.WriteLine($"Project saved to '{path}'.");
    }

    static void Dir(string? filter = null)
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
            Console.WriteLine($"Failed to list directory '{currentDirectory}': {ex.Message}");
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
            Console.WriteLine($"{type} {entry.Name}");
            printedAny = true;
        }

        if (!printedAny)
        {
            Console.WriteLine("<EMPTY>");
        }
    }
}


//launchpad.SetPad(0, 0, LaunchpadColors.Green);   // top-left pad
//        launchpad.SetSideButton(LaunchpadLayout.ClockButtonCc, LaunchpadColors.Amber);
//        launchpad.ClearGrid();
