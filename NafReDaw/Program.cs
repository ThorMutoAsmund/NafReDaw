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
    private static DawMode dawMode = DawMode.Play;
    private static SubMode subMode = SubMode.Play;
    private static EditTool editTool = EditTool.None;
    private static AsioSampleEngine audioEngine = new AsioSampleEngine();
    private static int currentlyPlayingSampleHandle = -1;
    private static int currentlyPlayingNote = -1;
    private static int currentlySelectedNote = -1;
    private const int LongTrimSeconds = 100;
    private const int ShortTrimSeconds = 10;

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
            CreateLaunchPadDevice();
            ApplyAudioDevice();
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

                            CreateLaunchPadDevice(
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
                            SetDawMode(DawMode.Play);
                            break;
                        }
                    case "record":
                        {
                            SetDawMode(DawMode.Edit);
                            break;
                        }
                    case "arrange":
                        {
                            SetDawMode(DawMode.Arrange);
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
                    case "remove" when parameters.Length > 0:
                    case "r" when parameters.Length > 0:
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

                            RemoveSample(note);
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

                            ApplyAudioDevice();
                            break;
                        }
                    case "cls":
                        {
                            //launchpad.ClearAll();
                            RefreshLaunchpad();
                            break;
                        }
                    case "p" when parameters.Length >= 1:
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

                            var color = LaunchpadColors.Off;
                            if (parameters.Length > 1)
                            {
                                if (!TryParseLaunchpadColor(parameters[1], out color))
                                {
                                    Console.WriteLine($"Illegal color {parameters[1]}");
                                    break;
                                }
                            }

                            launchpad.SetPad(note, color);
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

    static void CreateLaunchPadDevice(
        int inputDeviceIndex = 0,
        int outputDeviceIndex = 0)
    {
        if (launchpad != null)
        {
            launchpad.Stop();
            launchpad.Dispose();
        }

        launchpad = new LaunchpadDevice();

        launchpad.PadPressed += (_, e) => HandleNotePadPressed(e);
        launchpad.PadReleased += (_, e) => HandleNotePadReleased(e);
        launchpad.SideButtonPressed += (_, e) => HandleSideButton(e);

        launchpad.Start(inputDeviceIndex: inputDeviceIndex, outputDeviceIndex: outputDeviceIndex);

        var inputName = LaunchpadDevice.ListInputDevices().FirstOrDefault(d => d.Index == inputDeviceIndex)?.Name ?? "<none>";
        var outputName = LaunchpadDevice.ListOutputDevices().FirstOrDefault(d => d.Index == outputDeviceIndex)?.Name ?? "<none>";
        Console.WriteLine($"MIDI devices connected (input '{inputName}', output '{outputName}')");
    }

    static void ApplyProject()
    {
        CreateLaunchPadDevice(
            inputDeviceIndex: project.MidiInputDeviceIndex,
            outputDeviceIndex: project.MidiOutputDeviceIndex
        );

        ApplyAudioDevice();
        LoadSamplesIntoMemory();
        RefreshLaunchpad();        
    }

    static void HandleNotePadPressed(LaunchpadPadEventArgs e)
    {
        if (Debugger.IsAttached)
        {
            Console.WriteLine($"Pad {e.Row},{e.Column} note 0x{e.NoteNumber:X2}");
        }

        if (dawMode is DawMode.Play or DawMode.Edit)
        {
            if (currentlySelectedNote != e.NoteNumber && currentlyPlayingNote != e.NoteNumber)
            {
                currentlySelectedNote = -1;
            }
            var loadedSample = project.LoadedSamples.FirstOrDefault(s => s.Note == e.NoteNumber);
            if (loadedSample?.InMemorySample is not null)
            {
                PlayLoadedSample(loadedSample);
            }

            if (subMode == SubMode.Edit && loadedSample is not null)
            {
                currentlySelectedNote = e.NoteNumber;
                if (Debugger.IsAttached && currentlySelectedNote != -1)
                {
                    Console.WriteLine($"Editing note 0x{currentlySelectedNote:X2}");
                }
                RefreshLaunchpad();
            }
            else if (subMode == SubMode.Record && loadedSample is null)
            {
                currentlySelectedNote = currentlySelectedNote == e.NoteNumber ? -1 : e.NoteNumber;
                if (Debugger.IsAttached && currentlySelectedNote != -1)
                {
                    Console.WriteLine($"Arming note 0x{currentlySelectedNote:X2}");
                }
                RefreshLaunchpad();
            }
        }
    }

    static void HandleSideButton(LaunchpadButtonEventArgs e)
    {
        if (Debugger.IsAttached)
        {
            Console.WriteLine($"CC 0x{e.ControllerNumber:X2}"); //  = {e.Value}
        }

        switch (e.ControllerNumber)
        {
            case LaunchpadLayout.SessionButtonCc:
                {
                    SetDawMode(DawMode.Play, SubMode.Play);
                    break;
                }
            case LaunchpadLayout.NoteButtonCc:
                {
                    SetDawMode(DawMode.Edit, SubMode.Edit, EditTool.None);
                    break;
                }
            case LaunchpadLayout.DeviceButtonCc:
                {
                    SetDawMode(DawMode.Arrange, SubMode.Arrange);
                    break;
                }
            case LaunchpadLayout.RecordArmButtonCc when dawMode == DawMode.Edit:
                {
                    SetSubMode(SubMode.Record);
                    break;
                }
            case LaunchpadLayout.TrackSelectButtonCc when dawMode == DawMode.Edit:
                {
                    SetSubMode(SubMode.Edit);
                    break;
                }
            case LaunchpadLayout.Row0ButtonCc when subMode == SubMode.Edit:
                {
                    SetEditTool(editTool == EditTool.Start ? EditTool.None : EditTool.Start);
                    break;
                }
            case LaunchpadLayout.Row1ButtonCc when subMode == SubMode.Edit:
                {
                    SetEditTool(editTool == EditTool.End ? EditTool.None : EditTool.End);
                    break;
                }
            case LaunchpadLayout.Row7ButtonCc when subMode == SubMode.Edit:
                {
                    ToggleLoop();

                    break;
                }
            case LaunchpadLayout.RecordButtonCc when subMode == SubMode.Record:
                {
                    ToggleSamplingRecording();

                    break;
                }
            case LaunchpadLayout.UndoButtonCc when audioEngine.IsRecording:
                {
                    CancelSamplingRecording();
                    break;
                }
            case LaunchpadLayout.UpButtonCc when subMode == SubMode.Edit && currentlySelectedNote != -1:
                {
                    if (editTool is EditTool.Start or EditTool.End)
                    {
                        TrimSample(
                            startMilliSeconds: editTool == EditTool.Start ? LongTrimSeconds : null,
                            endMilliSeconds: editTool == EditTool.End ? LongTrimSeconds : null);
                    }
                    break;
                }
            case LaunchpadLayout.DownButtonCc when subMode == SubMode.Edit && currentlySelectedNote != -1:
                {
                    if (editTool is EditTool.Start or EditTool.End)
                    {
                        TrimSample(
                            startMilliSeconds: editTool == EditTool.Start ? -LongTrimSeconds : null,
                            endMilliSeconds: editTool == EditTool.End ? -LongTrimSeconds : null);
                    }
                    break;
                }
            case LaunchpadLayout.RightButtonCc when subMode == SubMode.Edit && currentlySelectedNote != -1:
                {
                    if (editTool is EditTool.Start or EditTool.End)
                    {
                        TrimSample(
                            startMilliSeconds: editTool == EditTool.Start ? ShortTrimSeconds : null,
                            endMilliSeconds: editTool == EditTool.End ? ShortTrimSeconds : null);
                    }
                    break;
                }
            case LaunchpadLayout.LeftButtonCc when subMode == SubMode.Edit && currentlySelectedNote != -1:
                {
                    if (editTool is EditTool.Start or EditTool.End)
                    {
                        TrimSample(
                            startMilliSeconds: editTool == EditTool.Start ? -ShortTrimSeconds : null,
                            endMilliSeconds: editTool == EditTool.End ? -ShortTrimSeconds : null);
                    }
                    break;
                }
        }
    }

    static void SetDawMode(DawMode newDawMode, SubMode? newEditMode = null, EditTool? newEditTool = null)
    {
        audioEngine.StopAllPlayback();
        if (audioEngine.IsRecording)
        {
            audioEngine.StopRecording();
        }

        currentlyPlayingSampleHandle = -1;
        currentlyPlayingNote = -1;
        currentlySelectedNote = -1;
        dawMode = newDawMode;
        Console.WriteLine($"Mode: {dawMode}");

        if (newEditMode.HasValue)
        {
            SetSubMode(newEditMode.Value, newEditTool);
            return;
        }
        RefreshLaunchpad();
    }

    static void SetSubMode(SubMode newMode, EditTool? newEditTool = null)
    {
        subMode = newMode;
        Console.WriteLine($"Edit mode: {subMode}");

        if (newEditTool.HasValue)
        {
            SetEditTool(newEditTool.Value);
            return;
        }
        RefreshLaunchpad();
    }

    static void SetEditTool(EditTool newEditTool)
    {
        editTool = newEditTool;
        Console.WriteLine($"Tool: {editTool}");
        RefreshLaunchpad();
    }


    static void HandleNotePadReleased(LaunchpadPadEventArgs e)
    {
    }

    static void ToggleLoop()
    {
        if (currentlySelectedNote == -1)
        {
            return;
        }

        var sample = project.LoadedSamples.FirstOrDefault(s => s.Note == currentlySelectedNote);
        if (sample is null)
        {
            return;
        }

        sample.Loop = !sample.Loop;
        project.ChangesMade = true;
        RefreshLaunchpad();

        if (Debugger.IsAttached)
        {
            Console.WriteLine($"Loop note 0x{currentlySelectedNote:X2}: {sample.Loop}");
        }
    }

    static void ToggleSamplingRecording()
    {
        if (currentlySelectedNote == -1)
        {
            return;
        }

        if (audioEngine.IsRecording)
        {
            StopSamplingRecording();
        }
        else
        {
            StartSamplingRecording();
        }
    }

    static void StartSamplingRecording()
    {
        try
        {
            audioEngine.StartRecording();
            RefreshLaunchpad();
            Console.WriteLine($"Recording for note 0x{currentlySelectedNote:X2}...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start recording: {ex.Message}");
        }
    }

    static void StopSamplingRecording()
    {
        var samplesFolder = GetSamplesFolder(project);
        var destPath = GetUniqueRecordingPath(samplesFolder);
        var note = (byte)currentlySelectedNote;

        try
        {
            audioEngine.SaveRecording(destPath);
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("No audio recorded.");
            RefreshLaunchpad();
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save recording: {ex.Message}");
            RefreshLaunchpad();
            return;
        }

        AssignSampleFromPath(note, destPath);
        var sample = project.LoadedSamples.First(s => s.Note == note);
        sample.Loop = false;
        project.ChangesMade = true;
        RefreshLaunchpad();
        Console.WriteLine($"Recorded '{Path.GetFileName(destPath)}' to note 0x{note:X2}.");
    }

    static void CancelSamplingRecording()
    {
        audioEngine.StopRecording();
        RefreshLaunchpad();
        Console.WriteLine("Recording cancelled.");
    }

    static string GetUniqueRecordingPath(string samplesFolder)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var path = Path.Combine(samplesFolder, $"recording_{timestamp}.wav");
        for (int i = 1; File.Exists(path); i++)
        {
            path = Path.Combine(samplesFolder, $"recording_{timestamp}_{i}.wav");
        }

        return path;
    }

    static bool AssignSampleFromPath(byte note, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var sample = project.LoadedSamples.FirstOrDefault(s => s.Note == note);
        if (sample is null)
        {
            sample = new LoadedSample();
            project.LoadedSamples.Add(sample);
        }

        sample.Note = note;
        sample.FileName = fileName;
        sample.StartSample = 0;

        try
        {
            sample.InMemorySample = AsioSampleEngine.LoadSample(filePath);
            sample.EndSample = sample.InMemorySample.SampleCount;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load sample '{fileName}': {ex.Message}");
            sample.InMemorySample = null;
            sample.EndSample = 0;
            return false;
        }
    }

    static void TrimSample(int? startMilliSeconds = null, int? endMilliSeconds = null)
    {
        if (currentlySelectedNote == -1)
        {
            return;
        }

        var sample = project.LoadedSamples.FirstOrDefault(s => s.Note == currentlySelectedNote);
        if (sample?.InMemorySample is null)
        {
            return;
        }

        int sampleRate = sample.InMemorySample.WaveFormat.SampleRate;
        int totalSamples = sample.InMemorySample.SampleCount;
        int endSample = sample.EndSample > 0 ? sample.EndSample : totalSamples;
        bool changed = false;

        if (startMilliSeconds is int startMs)
        {
            int delta = (int)((long)startMs * sampleRate / 1000);
            int newStart = Math.Clamp(sample.StartSample + delta, 0, endSample - 1);
            if (newStart != sample.StartSample)
            {
                sample.StartSample = newStart;
                changed = true;
            }
        }

        if (endMilliSeconds is int endMs)
        {
            int delta = (int)((long)endMs * sampleRate / 1000);
            int newEnd = Math.Clamp(endSample + delta, sample.StartSample + 1, totalSamples);
            if (newEnd != endSample)
            {
                sample.EndSample = newEnd;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        project.ChangesMade = true;

        if (Debugger.IsAttached)
        {
            Console.WriteLine($"Trim note 0x{currentlySelectedNote:X2}: start={sample.StartSample}, end={sample.EndSample} ({totalSamples} samples)");
        }
    }

    static void PlayLoadedSample(LoadedSample sample)
    {
        if (sample.InMemorySample is null)
        {
            return;
        }

        try
        {
            if (currentlyPlayingSampleHandle != -1)
            {
                audioEngine.StopPlayback(currentlyPlayingSampleHandle);
                currentlyPlayingSampleHandle = -1;
                currentlyPlayingNote = -1;
            }

            currentlyPlayingNote = sample.Note;
            currentlyPlayingSampleHandle = audioEngine.PlayOneShot(sample.InMemorySample, sample.StartSample, sample.EndSample, sample.Loop, () =>
            {
                currentlyPlayingSampleHandle = -1;
                currentlyPlayingNote = -1;
                RefreshLaunchpad();
            });

            if (currentlyPlayingSampleHandle == -1)
            {
                currentlyPlayingNote = -1;
            }

            RefreshLaunchpad();
        }
        catch (Exception ex)
        {
            currentlyPlayingSampleHandle = -1;
            currentlyPlayingNote = -1;
            RefreshLaunchpad();
            Console.WriteLine($"Failed to play sample '{sample.FileName}': {ex.Message}");
        }
    }

    static byte GetPadColorForNote(int note)
    {
        if (currentlySelectedNote == note && subMode == SubMode.Record)
        {
            return LaunchpadColors.Red;
        }

        if (!project.LoadedSamples.Any(s => s.Note == note))
        {
            return LaunchpadColors.Off;
        }

        if (currentlySelectedNote == note && subMode == SubMode.Edit)
        {
            return LaunchpadColors.Blue;
        }

        if (currentlyPlayingNote == note)
        {
            return LaunchpadColors.GreenBright;
        }

        return LaunchpadColors.DimWhite;
    }

    static void ApplyAudioDevice()
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

    static void LoadSamplesIntoMemory()
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
        // Refresh pad buttons
        for (int row = 0; row < LaunchpadLayout.GridRows; row++)
        {
            for (int col = 0; col < LaunchpadLayout.GridColumns; col++)
            {
                var note = LaunchpadLayout.NoteFromGrid(row, col);
                launchpad.SetPad(row, col, GetPadColorForNote(note));
            }
        }

        // Refresh mode buttons
        launchpad.SetSideButton(LaunchpadLayout.SessionButtonCc, dawMode == DawMode.Play ? LaunchpadColors.Green : LaunchpadColors.Off);
        launchpad.SetSideButton(LaunchpadLayout.NoteButtonCc, dawMode == DawMode.Edit ? LaunchpadColors.Red : LaunchpadColors.Off);
        launchpad.SetSideButton(LaunchpadLayout.DeviceButtonCc, dawMode == DawMode.Arrange ? LaunchpadColors.Amber : LaunchpadColors.Off);

        // Refresh edit buttons
        launchpad.SetSideButton(LaunchpadLayout.RecordArmButtonCc, subMode == SubMode.Record  ? LaunchpadColors.Red : LaunchpadColors.Off);
        launchpad.SetSideButton(LaunchpadLayout.TrackSelectButtonCc, subMode == SubMode.Edit ? LaunchpadColors.GreenBright : LaunchpadColors.Off);

        launchpad.SetSideButton(LaunchpadLayout.RecordButtonCc, audioEngine.IsRecording ? LaunchpadColors.Red : LaunchpadColors.Off);

        launchpad.SetSideButton(LaunchpadLayout.Row0ButtonCc, subMode == SubMode.Edit && editTool == EditTool.Start ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
        launchpad.SetSideButton(LaunchpadLayout.Row1ButtonCc, subMode == SubMode.Edit && editTool == EditTool.End ? LaunchpadColors.GreenBright : LaunchpadColors.Off);

        var sample = project.LoadedSamples.FirstOrDefault(s => s.Note == currentlySelectedNote);
        launchpad.SetSideButton(LaunchpadLayout.Row7ButtonCc, subMode == SubMode.Edit && sample is not null && sample.Loop ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
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

        AssignSampleFromPath(note, destPath);
        project.ChangesMade = true;
        RefreshLaunchpad();
        Console.WriteLine($"Assigned '{destFileName}' to note 0x{note:X2}.");
    }

    static void RemoveSample(byte note)
    {
        var sample = project.LoadedSamples.FirstOrDefault(s => s.Note == note);
        if (sample is null)
        {
            Console.WriteLine($"No sample assigned to note 0x{note:X2}.");
            return;
        }

        project.LoadedSamples.Remove(sample);
        project.ChangesMade = true;
        RefreshLaunchpad();
        Console.WriteLine($"Removed sample from note 0x{note:X2}.");
    }

    static bool TryParseNote(string text, out byte note)
    {
        note = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var commaIndex = text.IndexOf(',');
        if (commaIndex >= 0)
        {
            var rowText = text[..commaIndex].Trim();
            var columnText = text[(commaIndex + 1)..].Trim();

            if (!int.TryParse(columnText, out var column) || !int.TryParse(rowText, out var row))
            {
                return false;
            }

            if (row < 0 || row >= LaunchpadLayout.GridRows || column < 0 || column >= LaunchpadLayout.GridColumns)
            {
                return false;
            }

            note = (byte)LaunchpadLayout.NoteFromGrid(row, column);
            return true;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return byte.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out note);
        }

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
