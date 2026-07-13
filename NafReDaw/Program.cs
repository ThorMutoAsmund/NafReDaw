
using NafMidi;
using NafAudio;
using System.Diagnostics;
using System.Text.Json;

namespace NafReDaw;
internal class Program
{
    private const string ProjectFileExtension = ".nafdaw";
    private const string DebugProjcetFolder = "C:\\Users\\thora\\Google Drive\\Music\\NafDaw\\Debug";
    private static DawProject project = new DawProject();
    private static LaunchpadDevice launchpad = null!;
    private static DawMode mode = DawMode.Play;
    private static AsioSampleEngine audioEngine = new AsioSampleEngine();

[STAThread]
    static void Main(string[] args)
    {
        if (Debugger.IsAttached && Directory.Exists(DebugProjcetFolder))
        {
            Directory.SetCurrentDirectory(DebugProjcetFolder);
        }

        var defaultProjectPath = new DirectoryInfo(Directory.GetCurrentDirectory()).Name + ProjectFileExtension;
        if (File.Exists(defaultProjectPath))
        {
            project = LoadProject(defaultProjectPath) ?? project;
            ApplyProject();
        }
        else
        {
            launchpad = CreateLaunchPadDevice();
            ApplyAudioDevice(project, audioEngine);
            RefreshLaunchpad();
        }

        Console.WriteLine("Ready!");

        var quit = false;
        try
        {
            while (!quit)
            {
                var command = Console.ReadLine();
                var parameters = SplitCommandLine(command);
                if (parameters.Length > 0)
                {
                    command = parameters[0];
                    parameters = parameters.Skip(1).ToArray();
                }

                switch (command?.ToLowerInvariant())
                {
                    case "q":
                    case "quit":
                        {
                            quit = AwaitQuitWhenChangesMade(project);
                            break;
                        }
                    case "m" when parameters.Length == 0:
                    case "midi" when parameters.Length == 0:
                        {
                            foreach (var d in LaunchpadDevice.ListInputDevices())
                                Console.WriteLine($"{d.Index}: {d.Name}");
                            break;
                        }
                    case "m" when parameters.Length != 0:
                    case "midi" when parameters.Length != 0:
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
                            launchpad = CreateLaunchPadDevice(
                                inputDeviceIndex: inputDeviceIndex,
                                outputDeviceIndex: outputDeviceIndex);
                            RefreshLaunchpad();
                            project.MidiInputDeviceIndex = inputDeviceIndex;
                            project.MidiOutputDeviceIndex = outputDeviceIndex;
                            project.ChangesMade = true;
                            break;
                        }
                    case "play":
                        {
                            SetMode(DawMode.Play);
                            break;
                        }
                    case "record":
                        {
                            SetMode(DawMode.Record);
                            break;
                        }
                    case "arrange":
                        {
                            SetMode(DawMode.Arrange);
                            break;
                        }
                    case "l":
                    case "load":
                        {
                            project = LoadProject(parameters.Length > 0 ? parameters[0] : null) ?? project;
                            ApplyProject();
                            break;
                        }
                    case "sample" when parameters.Length > 1:
                    case "s" when parameters.Length > 1:
                        {
                            if (!TryParseNote(parameters[0], out var note))
                            {
                                Console.WriteLine($"Illegal note {parameters[0]}");
                                break;
                            }
                            if (!LaunchpadLayout.IsGridNote(note))
                            {
                                Console.WriteLine($"Note 0x{note:X2} is not a launchpad grid note.");
                                break;
                            }

                            AddSample(note, parameters[1]);
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
                    case "a" when parameters.Length == 0:
                    case "audio" when parameters.Length == 0:
                        {
                            var drivers = AsioSampleEngine.GetDrivers();
                            for (int i = 0; i < drivers.Count; i++)
                            {
                                Console.WriteLine($"{i}: {drivers[i].Name}");
                            }
                            if (drivers.Count == 0)
                            {
                                Console.WriteLine("<EMPTY>");
                            }
                            break;
                        }
                    case "a" when parameters.Length != 0:
                    case "audio" when parameters.Length != 0:
                        {
                            if (!TryResolveAudioDeviceId(parameters.ElementAtOrDefault(0), out var playbackId))
                            {
                                Console.WriteLine($"Illegal index {parameters[0]}");
                                break;
                            }

                            var recordingId = playbackId;
                            if (parameters.Length > 1)
                            {
                                if (!TryResolveAudioDeviceId(parameters[1], out recordingId))
                                {
                                    Console.WriteLine($"Illegal index {parameters[1]}");
                                    break;
                                }
                            }

                            project.AudioPlaybackDeviceId = playbackId;
                            project.AudioRecordingDeviceId = recordingId;
                            project.ChangesMade = true;

                            ApplyAudioDevice(project, audioEngine);
                            break;
                        }
                    case "clr":
                    case "clear":
                        {
                            launchpad.ClearAll();
                            break;
                        }
                    case "p" when parameters.Length > 1:
                        {
                            if (!Int32.TryParse(parameters[0], out var row))
                            {
                                Console.WriteLine($"Illegal row {parameters[0]}");
                                break;
                            }
                            if (!Int32.TryParse(parameters[1], out var column))
                            {
                                Console.WriteLine($"Illegal column {parameters[1]}");
                                break;
                            }
                            
                            var color = LaunchpadColors.Off;
                            if (parameters.Length > 2)
                            {
                                if (!TryParseLaunchpadColor(parameters[2], out color))
                                {
                                    Console.WriteLine($"Illegal color {parameters[2]}");
                                    break;
                                }
                            }

                            launchpad.SetPad(row, column, color);
                            break;
                        }
                }
            }
        }
        finally
        {
            launchpad.Stop();
            launchpad.Dispose();
            audioEngine.Dispose();
        }

        Console.WriteLine("Goodbye!");
    }

    static bool AwaitQuitWhenChangesMade(DawProject project)
    {
        if (!project.ChangesMade)
        {
            return true;
        }

        while (true)
        {
            Console.WriteLine("Unsaved changes. Choose: (c)ancel, (q)uit anyway, (s)ave and quit.");
            var response = Console.ReadLine()?.Trim();

            switch (response?.ToLowerInvariant())
            {
                case null:
                case "":
                case "c":
                case "cancel":
                    Console.WriteLine("Quit cancelled.");
                    return false;
                case "q":
                case "quit":
                    return true;
                case "s":
                case "save":
                    SaveProject(project);
                    return true;
            }
        }
    }

    static LaunchpadDevice CreateLaunchPadDevice(
        int inputDeviceIndex = 0,
        int outputDeviceIndex = 0)
    {
        var launchpad = new LaunchpadDevice();

        launchpad.PadPressed += (_, e) => HandleNoteButton(e);        
        launchpad.SideButtonPressed += (_, e) => HandleSideButton(e);

        launchpad.Start(inputDeviceIndex: inputDeviceIndex, outputDeviceIndex: outputDeviceIndex);

        var inputName = LaunchpadDevice.ListInputDevices().FirstOrDefault(d => d.Index == inputDeviceIndex)?.Name ?? "<none>";
        var outputName = LaunchpadDevice.ListOutputDevices().FirstOrDefault(d => d.Index == outputDeviceIndex)?.Name ?? "<none>";
        Console.WriteLine($"MIDI devices connected (input '{inputName}', output '{outputName}')");

        return launchpad;
    }

    //static void SyncSession(DawProject project, ModeHolder mode, LaunchpadDevice launchpad)
    //{
    //    _project = project;
    //    _mode = mode;
    //    _launchpad = launchpad;
    //}

    static void ApplyProject()
    {
        launchpad?.Stop();
        launchpad?.Dispose();

        launchpad = CreateLaunchPadDevice(
            inputDeviceIndex: project.MidiInputDeviceIndex,
            outputDeviceIndex: project.MidiOutputDeviceIndex
        );

        ApplyAudioDevice(project, audioEngine);
        LoadSamplesIntoMemory(project);
        RefreshLaunchpad();        
    }

    static void HandleNoteButton(LaunchpadPadEventArgs e)
    {
        if (Debugger.IsAttached)
        {
            Console.WriteLine($"Pad {e.Row},{e.Column} note 0x{e.NoteNumber:X2}");
        }
    }

    static void HandleSideButton(LaunchpadButtonEventArgs e)
    {
        if (Debugger.IsAttached)
        {
            Console.WriteLine($"CC 0x{e.ControllerNumber:X2}"); //  = {e.Value}
        }

        var newMode = e.ControllerNumber switch
        {
            LaunchpadLayout.SessionButtonCc => DawMode.Play,
            LaunchpadLayout.NoteButtonCc => DawMode.Record,
            LaunchpadLayout.DeviceButtonCc => DawMode.Arrange,
            _ => default(DawMode?)
        };

        if (newMode is not null)
        {
            SetMode(newMode.Value);
        }
    }

    static void SetMode(DawMode newMode)
    {
        mode = newMode;
        Console.WriteLine($"Mode: {mode}");
        RefreshLaunchpad();
    }

    static void RefreshModeButtons(LaunchpadDevice launchpad, DawMode mode)
    {
        launchpad.SetSideButton(LaunchpadLayout.SessionButtonCc, mode == DawMode.Play ? LaunchpadColors.Green : LaunchpadColors.Off);
        launchpad.SetSideButton(LaunchpadLayout.NoteButtonCc, mode == DawMode.Record ? LaunchpadColors.Red : LaunchpadColors.Off);
        launchpad.SetSideButton(LaunchpadLayout.DeviceButtonCc, mode == DawMode.Arrange ? LaunchpadColors.Amber : LaunchpadColors.Off);
    }

    static void ApplyAudioDevice(DawProject project, AsioSampleEngine audioEngine)
    {
        audioEngine.Stop();
        audioEngine.PlaybackDeviceId = project.AudioPlaybackDeviceId;
        audioEngine.RecordingDeviceId = project.AudioRecordingDeviceId;

        var drivers = AsioSampleEngine.GetDrivers();
        if (drivers.Count == 0)
        {
            Console.WriteLine("No ASIO drivers found. Audio disabled — use 'audio' to list drivers when one is installed.");
            return;
        }

        try
        {
            audioEngine.StartPlayback();
            Console.WriteLine($"ASIO playback started (playback '{project.AudioPlaybackDeviceId}', recording '{project.AudioRecordingDeviceId}').");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start ASIO audio: {ex.Message}");
            Console.WriteLine("Audio disabled — connect your interface and run 'audio' to list available audio drivers.");
        }
    }

    static string GetSamplesFolder(DawProject project)
    {
        var samplesFolder = Path.Combine(Directory.GetCurrentDirectory(), project.SamplesFolder);
        Directory.CreateDirectory(samplesFolder);
        
        return samplesFolder;
    }


    static void LoadSamplesIntoMemory(DawProject project)
    {
        var baseFolder = GetSamplesFolder(project);
        foreach (var sample in project.LoadedSamples)
        {
            sample.InMemorySample = null;
            try
            {
                var fullPath = Path.Combine(baseFolder, sample.FileName);
                if (!File.Exists(fullPath))
                {
                    Console.WriteLine($"Sample file not found '{fullPath}'.");
                    continue;
                }

                sample.InMemorySample = AsioSampleEngine.LoadSample(fullPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load sample '{sample.FileName}': {ex.Message}");
            }
        }
    }

    static bool TryResolveAudioDeviceId(string? indexText, out string deviceId)
    {
        deviceId = "";

        if (string.IsNullOrWhiteSpace(indexText))
            return false;

        if (!int.TryParse(indexText, out var index))
            return false;

        var drivers = AsioSampleEngine.GetDrivers();
        if (index < 0 || index >= drivers.Count)
            return false;

        deviceId = drivers[index].Id;
        return true;
    }

    static void RefreshLaunchpad()
    {
        var sampleNotes = project.LoadedSamples.Select(s => s.Note).ToHashSet();

        for (int row = 0; row < LaunchpadLayout.GridRows; row++)
        {
            for (int col = 0; col < LaunchpadLayout.GridColumns; col++)
            {
                var note = LaunchpadLayout.NoteFromGrid(row, col);
                var color = sampleNotes.Contains((byte)note) ? LaunchpadColors.DimWhite : LaunchpadColors.Off;
                launchpad.SetPad(row, col, color);
            }
        }

        RefreshModeButtons(launchpad, mode);
    }

    static void AddSample(byte note, string sourceFile)
    {
        var sourcePath = Path.IsPathRooted(sourceFile)
            ? sourceFile
            : Path.Combine(Directory.GetCurrentDirectory(), sourceFile);

        if (!File.Exists(sourcePath))
        {
            Console.WriteLine($"File not found '{sourcePath}'.");
            return;
        }

        var samplesFolder = GetSamplesFolder(project);

        var destFileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(samplesFolder, destFileName);

        try
        {
            File.Copy(sourcePath, destPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to copy sample to '{destPath}': {ex.Message}");
            return;
        }

        var sample = project.LoadedSamples.FirstOrDefault(s => s.Note == note);
        if (sample is null)
        {
            sample = new LoadedSample();
            project.LoadedSamples.Add(sample);
        }

        sample.Note = note;
        sample.FileName = destFileName;
        sample.StartSample = 0;

        try
        {
            sample.InMemorySample = AsioSampleEngine.LoadSample(destPath);
            sample.EndSample = sample.InMemorySample.SampleCount;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load sample '{destFileName}': {ex.Message}");
            sample.InMemorySample = null;
            sample.EndSample = 0;
        }

        project.ChangesMade = true;
        RefreshLaunchpad();
        Console.WriteLine($"Assigned '{destFileName}' to note 0x{note:X2}.");
    }

    static bool TryParseNote(string text, out byte note)
    {
        note = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return byte.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out note);

        return byte.TryParse(text, out note);
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

    static string[] SplitCommandLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return [];

        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line.Trim())
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result.ToArray();
    }

    static bool TryParseLaunchpadColor(string text, out byte color)
    {
        color = LaunchpadColors.Off;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var field = typeof(LaunchpadColors).GetField(
            text,
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.IgnoreCase
        );

        if (field is null || field.FieldType != typeof(byte))
            return false;

        if (field.GetValue(null) is not byte b)
            return false;

        color = b;
        return true;
    }
}


//launchpad.SetPad(0, 0, LaunchpadColors.Green);   // top-left pad
//        launchpad.SetSideButton(LaunchpadLayout.ClockButtonCc, LaunchpadColors.Amber);
//        launchpad.ClearGrid();
