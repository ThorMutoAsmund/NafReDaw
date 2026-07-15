using NafMidi;
using NafAudio;
using System.Diagnostics;
using System.Text.Json;

namespace NafReDaw;


internal class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (Debugger.IsAttached && Directory.Exists(App.DebugProjcetFolder))
        {
            Directory.SetCurrentDirectory(App.DebugProjcetFolder);
        }

        var defaultProjectPath = new DirectoryInfo(Directory.GetCurrentDirectory()).Name + App.ProjectFileExtension;
        if (File.Exists(defaultProjectPath))
        {
            App.Project = FileSystem.LoadProject(defaultProjectPath) ?? App.Project;
            ApplyProject();
        }
        else
        {
            MidiSystem.CreateLaunchPadDevice(
                HandleNotePadPressed,
                HandleNotePadReleased,
                HandleSideButton);
            AudioSystem.ApplyAudioDevice();
            RefreshLaunchpad();
        }

        App.Output("Ready!");

        var quit = false;
        try
        {
            while (!quit)
            {
                var command = Console.ReadLine();
                var parameters = Helpers.SplitCommandLine(command);
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
                            quit = AwaitQuitWhenChangesMade();
                            break;
                        }
                    case "m" when parameters.Length == 0:
                    case "midi" when parameters.Length == 0:
                        {
                            foreach (var d in LaunchpadDevice.ListInputDevices())
                            {
                                App.Output($"{d.Index}: {d.Name}");
                            }
                            break;
                        }
                    case "m" when parameters.Length != 0:
                    case "midi" when parameters.Length != 0:
                        {
                            if (!Int32.TryParse(parameters[0], out var index))
                            {
                                App.Output($"Illegal index {parameters[0]}");
                                break;
                            }
                            var inputDeviceIndex = index;
                            var outputDeviceIndex = index;
                            if (parameters.Length > 1)
                            {
                                if (!Int32.TryParse(parameters[1], out index))
                                {
                                    App.Output($"Illegal index {parameters[1]}");
                                    break;
                                }
                                outputDeviceIndex = index;
                            }

                            MidiSystem.CreateLaunchPadDevice(
                                HandleNotePadPressed,
                                HandleNotePadReleased,
                                HandleSideButton,
                                inputDeviceIndex: inputDeviceIndex,
                                outputDeviceIndex: outputDeviceIndex);
                            RefreshLaunchpad();
                            App.Project.MidiInputDeviceIndex = inputDeviceIndex;
                            App.Project.MidiOutputDeviceIndex = outputDeviceIndex;
                            App.Project.ChangesMade = true;
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
                            App.Project = FileSystem.LoadProject(parameters.Length > 0 ? parameters[0] : null) ?? App.Project;
                            ApplyProject();
                            break;
                        }
                    case "sample" when parameters.Length > 1:
                    case "s" when parameters.Length > 1:
                        {
                            if (!TryParseNote(parameters[0], out var note))
                            {
                                App.Output($"Illegal note {parameters[0]}");
                                break;
                            }
                            if (!LaunchpadLayout.IsGridNote(note))
                            {
                                App.Output($"Note 0x{note:X2} is not a App.Launchpad grid note.");
                                break;
                            }

                            if (AudioSystem.AddSample(note, parameters[1]))
                            {
                                RefreshLaunchpad();
                            }
                            break;
                        }
                    case "remove" when parameters.Length > 0:
                    case "r" when parameters.Length > 0:
                        {
                            if (!TryParseNote(parameters[0], out var note))
                            {
                                App.Output($"Illegal note {parameters[0]}");
                                break;
                            }
                            if (!LaunchpadLayout.IsGridNote(note))
                            {
                                App.Output($"Note 0x{note:X2} is not a App.Launchpad grid note.");
                                break;
                            }

                            if (AudioSystem.RemoveSample(note))
                            { 
                                RefreshLaunchpad(); 
                            }
                            break;
                        }
                    case "s":
                    case "save":
                        {
                            FileSystem.SaveProject(parameters.Length > 0 ? parameters[0] : null);
                            break;
                        }
                    case "dir":
                    case "ls":
                        {
                            FileSystem.Dir(parameters.Length > 0 ? parameters[0] : null);
                            break;
                        }
                    case "a" when parameters.Length == 0:
                    case "audio" when parameters.Length == 0:
                        {
                            var drivers = AsioSampleEngine.GetDrivers();
                            for (int i = 0; i < drivers.Count; i++)
                            {
                                App.Output($"{i}: {drivers[i].Name}");
                            }
                            if (drivers.Count == 0)
                            {
                                App.Output("<EMPTY>");
                            }
                            break;
                        }
                    case "a" when parameters.Length != 0:
                    case "audio" when parameters.Length != 0:
                        {
                            if (!TryResolveAudioDeviceId(parameters.ElementAtOrDefault(0), out var playbackId))
                            {
                                App.Output($"Illegal index {parameters[0]}");
                                break;
                            }

                            var recordingId = playbackId;
                            if (parameters.Length > 1)
                            {
                                if (!TryResolveAudioDeviceId(parameters[1], out recordingId))
                                {
                                    App.Output($"Illegal index {parameters[1]}");
                                    break;
                                }
                            }

                            App.Project.AudioPlaybackDeviceId = playbackId;
                            App.Project.AudioRecordingDeviceId = recordingId;
                            App.Project.ChangesMade = true;

                            AudioSystem.ApplyAudioDevice();
                            break;
                        }
                    case "cls":
                        {
                            //App.Launchpad.ClearAll();
                            RefreshLaunchpad();
                            break;
                        }
                    case "p" when parameters.Length >= 1:
                        {
                            if (!TryParseNote(parameters[0], out var note))
                            {
                                App.Output($"Illegal note {parameters[0]}");
                                break;
                            }
                            if (!LaunchpadLayout.IsGridNote(note))
                            {
                                App.Output($"Note 0x{note:X2} is not a App.Launchpad grid note.");
                                break;
                            }

                            var color = LaunchpadColors.Off;
                            if (parameters.Length > 1)
                            {
                                if (!TryParseLaunchpadColor(parameters[1], out color))
                                {
                                    App.Output($"Illegal color {parameters[1]}");
                                    break;
                                }
                            }

                            App.Launchpad.SetPad(note, color);
                            break;
                        }
                }
            }
        }
        finally
        {
            App.Launchpad.Stop();
            App.Launchpad.Dispose();
            App.AudioEngine.Dispose();
        }

        App.Output("Goodbye!");
    }

    static bool AwaitQuitWhenChangesMade()
    {
        if (!App.Project.ChangesMade)
        {
            return true;
        }

        while (true)
        {
            App.Output("Unsaved changes. Choose: (c)ancel, (q)uit anyway, (s)ave and quit.");
            var response = Console.ReadLine()?.Trim();

            switch (response?.ToLowerInvariant())
            {
                case null:
                case "":
                case "c":
                case "cancel":
                    App.Output("Quit cancelled.");
                    return false;
                case "q":
                case "quit":
                    return true;
                case "s":
                case "save":
                    FileSystem.SaveProject();
                    return true;
            }
        }
    }

    static void ApplyProject()
    {
        MidiSystem.CreateLaunchPadDevice(
            HandleNotePadPressed,
            HandleNotePadReleased,
            HandleSideButton,
            inputDeviceIndex: App.Project.MidiInputDeviceIndex,
            outputDeviceIndex: App.Project.MidiOutputDeviceIndex
        );

        AudioSystem.ApplyAudioDevice();
        AudioSystem.LoadSamplesIntoMemory();
        
        RefreshLaunchpad();        
    }

    static void HandleNotePadPressed(LaunchpadPadEventArgs e)
    {
        App.Debug($"Pad {e.Row},{e.Column} note 0x{e.NoteNumber:X2}");

        if (App.DawMode is DawMode.Play or DawMode.Edit)
        {
            if (App.CurrentlySelectedNote != e.NoteNumber && App.CurrentlyPlayingNote != e.NoteNumber)
            {
                App.CurrentlySelectedNote = -1;
            }
            var loadedSample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == e.NoteNumber);
            if (loadedSample?.InMemorySample is not null)
            {
                if (AudioSystem.PlayLoadedSample(loadedSample, () => RefreshLaunchpad()))
                {
                    RefreshLaunchpad();
                }
            }

            if (App.SubMode == SubMode.Edit && loadedSample is not null)
            {
                App.CurrentlySelectedNote = e.NoteNumber;
                App.Debug($"Editing note 0x{App.CurrentlySelectedNote:X2}");
                RefreshLaunchpad();
            }
            else if (App.SubMode == SubMode.Record && loadedSample is null)
            {
                App.CurrentlySelectedNote = App.CurrentlySelectedNote == e.NoteNumber ? -1 : e.NoteNumber;
                if (App.CurrentlySelectedNote != -1)
                {
                    App.Debug($"Arming note 0x{App.CurrentlySelectedNote:X2}");
                }
                RefreshLaunchpad();
            }
        }
    }

    static void HandleSideButton(LaunchpadButtonEventArgs e)
    {
        App.Debug($"CC 0x{e.ControllerNumber:X2}"); //  = {e.Value}

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
            case LaunchpadLayout.RecordArmButtonCc when App.DawMode == DawMode.Edit:
                {
                    SetSubMode(SubMode.Record);
                    break;
                }
            case LaunchpadLayout.TrackSelectButtonCc when App.DawMode == DawMode.Edit:
                {
                    SetSubMode(SubMode.Edit);
                    break;
                }
            case LaunchpadLayout.Row0ButtonCc when App.SubMode == SubMode.Edit:
                {
                    SetEditTool(App.EditTool == EditTool.Start ? EditTool.None : EditTool.Start);
                    break;
                }
            case LaunchpadLayout.Row1ButtonCc when App.SubMode == SubMode.Edit:
                {
                    SetEditTool(App.EditTool == EditTool.End ? EditTool.None : EditTool.End);
                    break;
                }
            case LaunchpadLayout.Row7ButtonCc when App.SubMode == SubMode.Edit:
                {
                    if (AudioSystem.ToggleLoop())
                    {
                        RefreshLaunchpad();
                    }

                    break;
                }
            case LaunchpadLayout.RecordButtonCc when App.SubMode == SubMode.Record:
                {
                    ToggleSamplingRecording();

                    break;
                }
            case LaunchpadLayout.UndoButtonCc when App.AudioEngine.IsRecording:
                {
                    AudioSystem.CancelSamplingRecording();
                    RefreshLaunchpad();
                    break;
                }
            case LaunchpadLayout.UpButtonCc when App.SubMode == SubMode.Edit && App.CurrentlySelectedNote != -1:
                {
                    if (App.EditTool is EditTool.Start or EditTool.End)
                    {
                        AudioSystem.TrimSample(
                            startMilliSeconds: App.EditTool == EditTool.Start ? App.LongTrimSeconds : null,
                            endMilliSeconds: App.EditTool == EditTool.End ? App.LongTrimSeconds : null);
                    }
                    break;
                }
            case LaunchpadLayout.DownButtonCc when App.SubMode == SubMode.Edit && App.CurrentlySelectedNote != -1:
                {
                    if (App.EditTool is EditTool.Start or EditTool.End)
                    {
                        AudioSystem.TrimSample(
                            startMilliSeconds: App.EditTool == EditTool.Start ? -App.LongTrimSeconds : null,
                            endMilliSeconds: App.EditTool == EditTool.End ? -App.LongTrimSeconds : null);
                    }
                    break;
                }
            case LaunchpadLayout.RightButtonCc when App.SubMode == SubMode.Edit && App.CurrentlySelectedNote != -1:
                {
                    if (App.EditTool is EditTool.Start or EditTool.End)
                    {
                        AudioSystem.TrimSample(
                            startMilliSeconds: App.EditTool == EditTool.Start ? App.ShortTrimSeconds : null,
                            endMilliSeconds: App.EditTool == EditTool.End ? App.ShortTrimSeconds : null);
                    }
                    break;
                }
            case LaunchpadLayout.LeftButtonCc when App.SubMode == SubMode.Edit && App.CurrentlySelectedNote != -1:
                {
                    if (App.EditTool is EditTool.Start or EditTool.End)
                    {
                        AudioSystem.TrimSample(
                            startMilliSeconds: App.EditTool == EditTool.Start ? -App.ShortTrimSeconds : null,
                            endMilliSeconds: App.EditTool == EditTool.End ? -App.ShortTrimSeconds : null);
                    }
                    break;
                }
        }
    }

    static void SetDawMode(DawMode newDawMode, SubMode? newEditMode = null, EditTool? newEditTool = null)
    {
        App.AudioEngine.StopAllPlayback();
        if (App.AudioEngine.IsRecording)
        {
            App.AudioEngine.StopRecording();
        }

        App.CurrentlyPlayingSampleHandle = -1;
        App.CurrentlyPlayingNote = -1;
        App.CurrentlySelectedNote = -1;
        App.DawMode = newDawMode;
        App.Output($"Mode: {App.DawMode}");

        if (newEditMode.HasValue)
        {
            SetSubMode(newEditMode.Value, newEditTool);
            return;
        }
        RefreshLaunchpad();
    }

    static void SetSubMode(SubMode newMode, EditTool? newEditTool = null)
    {
        if (App.SubMode != newMode)
        {
            App.SubMode = newMode;
            App.CurrentlySelectedNote = -1;
            App.Output($"Edit mode: {App.SubMode}");
        }

        if (newEditTool.HasValue)
        {
            SetEditTool(newEditTool.Value);
            return;
        }
        RefreshLaunchpad();
    }

    static void SetEditTool(EditTool newEditTool)
    {
        if (App.EditTool != newEditTool)
        {
            App.EditTool = newEditTool;
            App.Output($"Tool: {App.EditTool}");
        }
        RefreshLaunchpad();
    }


    static void HandleNotePadReleased(LaunchpadPadEventArgs e)
    {
    }

    static void ToggleSamplingRecording()
    {
        if (App.CurrentlySelectedNote == -1)
        {
            return;
        }

        if (App.AudioEngine.IsRecording)
        {
            AudioSystem.StopSamplingRecording();
            RefreshLaunchpad();

        }
        else
        {
            if (AudioSystem.StartSamplingRecording())
            {
                RefreshLaunchpad();
            }
        }
    }

    static byte GetPadColorForNote(int note)
    {
        if (App.CurrentlySelectedNote == note && App.SubMode == SubMode.Record)
        {
            return LaunchpadColors.Red;
        }

        if (!App.Project.LoadedSamples.Any(s => s.Note == note))
        {
            return LaunchpadColors.Off;
        }

        if (App.CurrentlySelectedNote == note && App.SubMode == SubMode.Edit)
        {
            return LaunchpadColors.Blue;
        }

        if (App.CurrentlyPlayingNote == note)
        {
            return LaunchpadColors.GreenBright;
        }

        return LaunchpadColors.DimWhite;
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
                App.Launchpad.SetPad(row, col, GetPadColorForNote(note));
            }
        }

        // Refresh mode buttons
        App.Launchpad.SetSideButton(LaunchpadLayout.SessionButtonCc, App.DawMode == DawMode.Play ? LaunchpadColors.Green : LaunchpadColors.Off);
        App.Launchpad.SetSideButton(LaunchpadLayout.NoteButtonCc, App.DawMode == DawMode.Edit ? LaunchpadColors.Red : LaunchpadColors.Off);
        App.Launchpad.SetSideButton(LaunchpadLayout.DeviceButtonCc, App.DawMode == DawMode.Arrange ? LaunchpadColors.Amber : LaunchpadColors.Off);

        // Refresh edit buttons
        App.Launchpad.SetSideButton(LaunchpadLayout.RecordArmButtonCc, App.SubMode == SubMode.Record  ? LaunchpadColors.Red : LaunchpadColors.Off);
        App.Launchpad.SetSideButton(LaunchpadLayout.TrackSelectButtonCc, App.SubMode == SubMode.Edit ? LaunchpadColors.GreenBright : LaunchpadColors.Off);

        // Refresh record button
        App.Launchpad.SetSideButton(LaunchpadLayout.RecordButtonCc, App.AudioEngine.IsRecording ? LaunchpadColors.Red : LaunchpadColors.Off);

        // Refresh tool button
        var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == App.CurrentlySelectedNote);
        App.Launchpad.SetSideButton(LaunchpadLayout.Row0ButtonCc, App.SubMode == SubMode.Edit && App.EditTool == EditTool.Start ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
        App.Launchpad.SetSideButton(LaunchpadLayout.Row1ButtonCc, App.SubMode == SubMode.Edit && App.EditTool == EditTool.End ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
        App.Launchpad.SetSideButton(LaunchpadLayout.Row7ButtonCc, App.SubMode == SubMode.Edit && sample?.Loop == true ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
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
