using NafAudio;
using NafMidi;
using System.Diagnostics;

namespace NafReDaw;


internal class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ArrangeSystem.OnTransportTick = RefreshLaunchpad;

        if (Debugger.IsAttached && Directory.Exists(App.DebugProjcetFolder))
        {
            Directory.SetCurrentDirectory(App.DebugProjcetFolder);
        }

        var defaultProjectPath = new DirectoryInfo(Directory.GetCurrentDirectory()).Name + App.ProjectFileExtension;
        if (File.Exists(defaultProjectPath))
        {
            App.Project = Project.LoadProject(defaultProjectPath) ?? App.Project;
            ApplyProject();
        }
        else
        {
            MidiSystem.CreateLaunchPadDevice(
                HandleNotePadPressed,
                HandleNotePadReleased,
                HandleSideButton,
                HandleSideButtonReleased);
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
                                HandleSideButtonReleased,
                                inputDeviceIndex: inputDeviceIndex,
                                outputDeviceIndex: outputDeviceIndex);
                            RefreshLaunchpad();
                            App.Project.MidiInputDeviceIndex = inputDeviceIndex;
                            App.Project.MidiOutputDeviceIndex = outputDeviceIndex;
                            App.ChangesMade = true;
                            break;
                        }
                    case "play":
                        {
                            SetDawMode(DawMode.Play, SubMode.Playing);
                            break;
                        }
                    case "record":
                        {
                            SetDawMode(DawMode.Edit, SubMode.Editing, EditTool.None);
                            break;
                        }
                    case "arrange":
                        {
                            SetDawMode(DawMode.Arrange, SubMode.Arranging);
                            break;
                        }
                    case "l":
                    case "load":
                        {
                            App.Project = Project.LoadProject(parameters.Length > 0 ? parameters[0] : null) ?? App.Project;
                            ApplyProject();
                            break;
                        }
                    case "sample" when parameters.Length > 1:
                    case "s" when parameters.Length > 1:
                        {
                            if (!Helpers.TryParseNote(parameters[0], out var note))
                            {
                                App.Output($"Illegal note {parameters[0]}");
                                break;
                            }
                            if (!LaunchpadLayout.IsGridNote(note))
                            {
                                App.Output($"Note 0x{note:X2} is not a launchpad grid note.");
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
                            if (!Helpers.TryParseNote(parameters[0], out var note))
                            {
                                App.Output($"Illegal note {parameters[0]}");
                                break;
                            }
                            if (!LaunchpadLayout.IsGridNote(note))
                            {
                                App.Output($"Note 0x{note:X2} is not a launchpad grid note.");
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
                            Project.SaveProject(parameters.Length > 0 ? parameters[0] : null);
                            break;
                        }
                    case "dir":
                    case "ls":
                        {
                            Project.Dir(parameters.Length > 0 ? parameters[0] : null);
                            break;
                        }
                    case "a" when parameters.Length == 0:
                    case "audio" when parameters.Length == 0:
                        {
                            var drivers = AsioSampleEngine.GetDrivers();
                            for (var i = 0; i < drivers.Count; i++)
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
                            App.ChangesMade = true;

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
                            if (!Helpers.TryParseNote(parameters[0], out var note))
                            {
                                App.Output($"Illegal note {parameters[0]}");
                                break;
                            }
                            if (!LaunchpadLayout.IsGridNote(note))
                            {
                                App.Output($"Note 0x{note:X2} is not a launchpad grid note.");
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
            ArrangeSystem.StopTransport();
            StopRecordingSubModeServices();
            App.Launchpad.Stop();
            App.Launchpad.Dispose();
            App.AudioEngine.Dispose();
        }

        App.Output("Goodbye!");
    }

    static bool AwaitQuitWhenChangesMade()
    {
        if (!App.ChangesMade)
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
                    Project.SaveProject();
                    return true;
            }
        }
    }

    static void ApplyProject()
    {
        ArrangeSystem.StopTransport();
        App.ActivePatternIndex = 0;
        App.ArrangeStepPage = 0;

        MidiSystem.CreateLaunchPadDevice(
            HandleNotePadPressed,
            HandleNotePadReleased,
            HandleSideButton,
            HandleSideButtonReleased,
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
            var loadedSample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == e.NoteNumber);
            if (loadedSample?.InMemorySample is not null)
            {
                if (AudioSystem.PlayLoadedSample(loadedSample, () => RefreshLaunchpad()))
                {
                    RefreshLaunchpad();
                }
            }

            if (App.SubMode == SubMode.Editing && loadedSample is not null)
            {
                App.CurrentlySelectedNote = e.NoteNumber;
                App.Debug($"Editing note 0x{App.CurrentlySelectedNote:X2}");
                RefreshLaunchpad();
            }
            else if (App.SubMode == SubMode.Recording && loadedSample is null)
            {
                App.CurrentlySelectedNote = App.CurrentlySelectedNote == e.NoteNumber ? -1 : e.NoteNumber;
                if (App.CurrentlySelectedNote != -1)
                {
                    App.Debug($"Arming note 0x{App.CurrentlySelectedNote:X2}");
                }
                RefreshLaunchpad();
            }
        }
        else if (App.SubMode == SubMode.Arranging)
        {
            if (App.IsRecordHeld)
            {
                var loadedSample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == e.NoteNumber);
                if (loadedSample is null)
                {
                    return;
                }

                App.ArrangePaintNote = e.NoteNumber;
                App.Output($"Arrange paint note 0x{App.ArrangePaintNote:X2}");
                if (loadedSample.InMemorySample is not null)
                {
                    AudioSystem.PlayLoadedSample(loadedSample, () => { }, replaceCurrent: true);
                }

                RefreshLaunchpad();
                return;
            }

            if (App.ArrangePaintNote == -1)
            {
                return;
            }

            if (ArrangeSystem.PaintOrClearStep(e.Row, e.Column, App.ArrangePaintNote))
            {
                RefreshLaunchpad();
            }
        }
    }

    static void HandleSideButtonReleased(LaunchpadButtonEventArgs e)
    {
        if (e.ControllerNumber == LaunchpadLayout.RecordButtonCc && App.SubMode == SubMode.Arranging)
        {
            RefreshLaunchpad();
        }
    }

    static void HandleSideButton(LaunchpadButtonEventArgs e)
    {
        App.Debug($"CC 0x{e.ControllerNumber:X2}"); //  = {e.Value}

        switch (e.ControllerNumber)
        {
            case LaunchpadLayout.SessionButtonCc:
                {
                    SetDawMode(DawMode.Play, SubMode.Playing);
                    break;
                }
            case LaunchpadLayout.NoteButtonCc:
                {
                    SetDawMode(DawMode.Edit, SubMode.Editing, EditTool.None);
                    break;
                }
            case LaunchpadLayout.DeviceButtonCc:
                {
                    SetDawMode(DawMode.Arrange, SubMode.Arranging);
                    break;
                }
            case LaunchpadLayout.RecordArmButtonCc when App.DawMode == DawMode.Edit:
                {
                    SetSubMode(SubMode.Recording);
                    break;
                }
            case LaunchpadLayout.TrackSelectButtonCc when App.DawMode == DawMode.Edit:
                {
                    SetSubMode(SubMode.Editing);
                    break;
                }
            case LaunchpadLayout.Row0ButtonCc when App.SubMode == SubMode.Recording:
                {
                    App.RecordMono = !App.RecordMono;
                    App.AudioEngine.RecordMono = App.RecordMono;
                    App.Output(App.RecordMono ? "Recording mode: mono" : "Recording mode: stereo");
                    RefreshLaunchpad();
                    break;
                }
            case LaunchpadLayout.Row0ButtonCc when App.SubMode == SubMode.Editing:
                {
                    SetEditTool(App.EditTool == EditTool.Start ? EditTool.None : EditTool.Start);
                    break;
                }
            case LaunchpadLayout.Row1ButtonCc when App.SubMode == SubMode.Editing:
                {
                    SetEditTool(App.EditTool == EditTool.End ? EditTool.None : EditTool.End);
                    break;
                }
            case LaunchpadLayout.Row2ButtonCc when App.SubMode == SubMode.Editing:
                {
                    SetEditTool(App.EditTool == EditTool.Volume ? EditTool.None : EditTool.Volume);
                    break;
                }
            case LaunchpadLayout.Row6ButtonCc when App.SubMode == SubMode.Editing:
                {
                    if (AudioSystem.TogglePlayBackwards())
                    {
                        RefreshLaunchpad();
                    }

                    break;
                }
            case LaunchpadLayout.Row7ButtonCc when App.SubMode == SubMode.Editing:
                {
                    if (AudioSystem.ToggleLoop())
                    {
                        RefreshLaunchpad();
                    }

                    break;
                }
            case LaunchpadLayout.QuantizeButtonCc when App.SubMode == SubMode.Editing && App.CurrentlySelectedNote != -1:
                {
                    if (AudioSystem.TrimSilence())
                    {
                        var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == App.CurrentlySelectedNote);
                        if (sample?.InMemorySample is not null)
                        {
                            AudioSystem.PlayLoadedSample(sample, () => RefreshLaunchpad());
                        }

                        App.Launchpad.PulseSideButton(LaunchpadLayout.QuantizeButtonCc, LaunchpadColors.GreenBright);
                        RefreshLaunchpad();
                    }

                    break;
                }
            case LaunchpadLayout.DeleteButtonCc when App.SubMode == SubMode.Editing && App.CurrentlySelectedNote != -1:
                {
                    var note = (byte)App.CurrentlySelectedNote;
                    if (App.CurrentlyPlayingNote == note)
                    {
                        App.AudioEngine.StopAllPlayback();
                        App.CurrentlyPlayingSampleHandle = -1;
                        App.CurrentlyPlayingNote = -1;
                    }

                    if (AudioSystem.RemoveSample(note))
                    {
                        App.CurrentlySelectedNote = -1;
                        if (App.ArrangePaintNote == note)
                        {
                            App.ArrangePaintNote = -1;
                        }

                        App.Launchpad.PulseSideButton(LaunchpadLayout.DeleteButtonCc, LaunchpadColors.Red);
                        RefreshLaunchpad();
                    }

                    break;
                }
            case LaunchpadLayout.RecordButtonCc when App.SubMode == SubMode.Recording:
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
            case LaunchpadLayout.RecordButtonCc when App.SubMode == SubMode.Arranging:
                {
                    RefreshLaunchpad();
                    break;
                }
            case LaunchpadLayout.ClickButtonCc when App.SubMode == SubMode.Arranging:
                {
                    if (App.IsShiftHeld)
                    {
                        ArrangeSystem.PlayFromActivePattern();
                    }
                    else
                    {
                        ArrangeSystem.ToggleTransport();
                    }

                    RefreshLaunchpad();
                    break;
                }
            case LaunchpadLayout.UpButtonCc when App.SubMode == SubMode.Arranging:
                {
                    ArrangeSystem.SelectNextPattern();
                    RefreshLaunchpad();
                    break;
                }
            case LaunchpadLayout.DownButtonCc when App.SubMode == SubMode.Arranging:
                {
                    if (App.IsShiftHeld)
                    {
                        ArrangeSystem.SelectFirstPattern();
                    }
                    else
                    {
                        ArrangeSystem.SelectPreviousPattern();
                    }

                    RefreshLaunchpad();
                    break;
                }
            case LaunchpadLayout.LeftButtonCc when App.SubMode == SubMode.Arranging:
                {
                    if (App.IsShiftHeld)
                    {
                        ArrangeSystem.FirstStepPage();
                    }
                    else
                    {
                        ArrangeSystem.PreviousStepPage();
                    }

                    RefreshLaunchpad();
                    break;
                }
            case LaunchpadLayout.RightButtonCc when App.SubMode == SubMode.Arranging:
                {
                    ArrangeSystem.NextStepPage();
                    RefreshLaunchpad();
                    break;
                }
            case LaunchpadLayout.UpButtonCc when App.SubMode == SubMode.Editing && App.CurrentlySelectedNote != -1:
                {
                    if (App.EditTool is EditTool.Start or EditTool.End)
                    {
                        TrimSampleAndReplay(
                            startMilliSeconds: App.EditTool == EditTool.Start ? App.LongTrimSeconds : null,
                            endMilliSeconds: App.EditTool == EditTool.End ? App.LongTrimSeconds : null);
                    }
                    else if (App.EditTool == EditTool.Volume)
                    {
                        AdjustVolumeAndReplay(App.LongVolumeStep);
                    }
                    break;
                }
            case LaunchpadLayout.DownButtonCc when App.SubMode == SubMode.Editing && App.CurrentlySelectedNote != -1:
                {
                    if (App.EditTool is EditTool.Start or EditTool.End)
                    {
                        TrimSampleAndReplay(
                            startMilliSeconds: App.EditTool == EditTool.Start ? -App.LongTrimSeconds : null,
                            endMilliSeconds: App.EditTool == EditTool.End ? -App.LongTrimSeconds : null);
                    }
                    else if (App.EditTool == EditTool.Volume)
                    {
                        AdjustVolumeAndReplay(-App.LongVolumeStep);
                    }
                    break;
                }
            case LaunchpadLayout.RightButtonCc when App.SubMode == SubMode.Editing && App.CurrentlySelectedNote != -1:
                {
                    if (App.EditTool is EditTool.Start or EditTool.End)
                    {
                        TrimSampleAndReplay(
                            startMilliSeconds: App.EditTool == EditTool.Start ? App.ShortTrimSeconds : null,
                            endMilliSeconds: App.EditTool == EditTool.End ? App.ShortTrimSeconds : null);
                    }
                    else if (App.EditTool == EditTool.Volume)
                    {
                        AdjustVolumeAndReplay(App.ShortVolumeStep);
                    }
                    break;
                }
            case LaunchpadLayout.LeftButtonCc when App.SubMode == SubMode.Editing && App.CurrentlySelectedNote != -1:
                {
                    if (App.EditTool is EditTool.Start or EditTool.End)
                    {
                        TrimSampleAndReplay(
                            startMilliSeconds: App.EditTool == EditTool.Start ? -App.ShortTrimSeconds : null,
                            endMilliSeconds: App.EditTool == EditTool.End ? -App.ShortTrimSeconds : null);
                    }
                    else if (App.EditTool == EditTool.Volume)
                    {
                        AdjustVolumeAndReplay(-App.ShortVolumeStep);
                    }
                    break;
                }
        }
    }

    static void TrimSampleAndReplay(int? startMilliSeconds, int? endMilliSeconds)
    {
        if (!AudioSystem.TrimSample(startMilliSeconds, endMilliSeconds))
        {
            return;
        }

        var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == App.CurrentlySelectedNote);
        if (sample?.InMemorySample is null)
        {
            return;
        }

        if (startMilliSeconds is not null)
        {
            AudioSystem.PlayLoadedSample(sample, () => RefreshLaunchpad());
        }
        else
        {
            var sampleRate = sample.InMemorySample.WaveFormat.SampleRate;
            var totalSamples = sample.InMemorySample.SampleCount;
            var end = sample.EndSample > 0 ? sample.EndSample : totalSamples;
            var replayStart = Math.Max(sample.StartSample, end - sampleRate);
            AudioSystem.PlayLoadedSample(sample, () => RefreshLaunchpad(), replayStart);
        }

        RefreshLaunchpad();
    }

    static void AdjustVolumeAndReplay(float delta)
    {
        if (!AudioSystem.AdjustSampleVolume(delta))
        {
            return;
        }

        var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == App.CurrentlySelectedNote);
        if (sample?.InMemorySample is null)
        {
            return;
        }

        AudioSystem.PlayLoadedSample(sample, () => RefreshLaunchpad());
        RefreshLaunchpad();
    }

    static void SetDawMode(DawMode newDawMode, SubMode? newEditMode = null, EditTool? newEditTool = null)
    {
        App.AudioEngine.StopAllPlayback();
        if (App.AudioEngine.IsRecording)
        {
            App.AudioEngine.StopRecording();
        }

        StopRecordingSubModeServices();
        ArrangeSystem.StopTransport();

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
        var previousMode = App.SubMode;
        if (App.SubMode != newMode)
        {
            App.SubMode = newMode;
            App.CurrentlySelectedNote = -1;
            App.Output($"Edit mode: {App.SubMode}");
        }

        if (previousMode == SubMode.Recording && newMode != SubMode.Recording)
        {
            StopRecordingSubModeServices();
        }
        else if (previousMode != SubMode.Recording && newMode == SubMode.Recording)
        {
            StartRecordingSubModeServices();
        }

        if (previousMode == SubMode.Arranging && newMode != SubMode.Arranging)
        {
            ArrangeSystem.StopTransport();
        }

        if (newEditTool.HasValue)
        {
            SetEditTool(newEditTool.Value);
            return;
        }
        RefreshLaunchpad();
    }

    static void StartRecordingSubModeServices()
    {
        AudioSystem.StartInputLevelMonitoring();
        MidiSystem.StartVuMeter();
    }

    static void StopRecordingSubModeServices()
    {
        MidiSystem.StopVuMeter();
        AudioSystem.StopInputLevelMonitoring();
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
        if (App.SubMode == SubMode.Arranging)
        {
            if (App.IsRecordHeld)
            {
                if (!App.Project.LoadedSamples.Any(s => s.Note == note))
                {
                    return LaunchpadColors.Off;
                }

                if (App.ArrangePaintNote == note)
                {
                    return LaunchpadColors.GreenBright;
                }

                return LaunchpadColors.DimWhite;
            }

            var grid = LaunchpadLayout.GridFromNote(note);
            if (grid is null)
            {
                return LaunchpadColors.Off;
            }

            var (track, column) = grid.Value;

            // Full-column white playhead when the current step is on this page.
            if (ArrangeSystem.IsPlayheadColumn(column))
            {
                return LaunchpadColors.DimWhite;
            }

            if (ArrangeSystem.IsCellAssigned(track, column))
            {
                return LaunchpadColors.Green;
            }

            return LaunchpadColors.Off;
        }

        if (App.CurrentlySelectedNote == note && App.SubMode == SubMode.Recording)
        {
            return LaunchpadColors.Red;
        }

        if (!App.Project.LoadedSamples.Any(s => s.Note == note))
        {
            return LaunchpadColors.Off;
        }

        if (App.CurrentlySelectedNote == note && App.SubMode == SubMode.Editing)
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
        for (var row = 0; row < LaunchpadLayout.GridRows; row++)
        {
            for (var col = 0; col < LaunchpadLayout.GridColumns; col++)
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
        if (App.SubMode == SubMode.Recording)
        {
            App.Launchpad.SetSideButton(
                LaunchpadLayout.RecordArmButtonCc,
                App.RecordMono ? LaunchpadColors.Green : LaunchpadColors.Red);
        }
        else
        {
            App.Launchpad.SetSideButton(LaunchpadLayout.RecordArmButtonCc, LaunchpadColors.Off);
        }
        App.Launchpad.SetSideButton(LaunchpadLayout.TrackSelectButtonCc, App.SubMode == SubMode.Editing ? LaunchpadColors.GreenBright : LaunchpadColors.Off);

        // Refresh record button
        var recordLit = App.AudioEngine.IsRecording
            || (App.SubMode == SubMode.Arranging && App.IsRecordHeld);
        App.Launchpad.SetSideButton(LaunchpadLayout.RecordButtonCc, recordLit ? LaunchpadColors.Red : LaunchpadColors.Off);

        App.Launchpad.SetSideButton(LaunchpadLayout.UpButtonCc, LaunchpadColors.Off);
        App.Launchpad.SetSideButton(LaunchpadLayout.DownButtonCc, LaunchpadColors.Off);
        App.Launchpad.SetSideButton(LaunchpadLayout.LeftButtonCc, LaunchpadColors.Off);
        App.Launchpad.SetSideButton(LaunchpadLayout.RightButtonCc, LaunchpadColors.Off);
        App.Launchpad.SetSideButton(LaunchpadLayout.ClickButtonCc, LaunchpadColors.Off);

        if (App.SubMode == SubMode.Recording)
        {
            MidiSystem.UpdateVuMeter();
        }
        else if (App.SubMode == SubMode.Editing)
        {
            var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == App.CurrentlySelectedNote);
            App.Launchpad.SetSideButton(LaunchpadLayout.Row0ButtonCc, App.EditTool == EditTool.Start ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
            App.Launchpad.SetSideButton(LaunchpadLayout.Row1ButtonCc, App.EditTool == EditTool.End ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
            App.Launchpad.SetSideButton(LaunchpadLayout.Row2ButtonCc, App.EditTool == EditTool.Volume ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
            App.Launchpad.SetSideButton(LaunchpadLayout.Row6ButtonCc, sample?.PlayBackwards == true ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
            App.Launchpad.SetSideButton(LaunchpadLayout.Row7ButtonCc, sample?.Loop == true ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
        }
        else if (App.SubMode == SubMode.Arranging)
        {
            App.Launchpad.SetSideButton(LaunchpadLayout.ClickButtonCc, ArrangeSystem.IsPlaying ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
            App.Launchpad.SetSideButton(
                LaunchpadLayout.UpButtonCc,
                ArrangeSystem.CanSelectNextPattern ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
            App.Launchpad.SetSideButton(
                LaunchpadLayout.DownButtonCc,
                ArrangeSystem.CanSelectPreviousPattern ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
            App.Launchpad.SetSideButton(
                LaunchpadLayout.LeftButtonCc,
                ArrangeSystem.CanPreviousStepPage ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
            App.Launchpad.SetSideButton(
                LaunchpadLayout.RightButtonCc,
                ArrangeSystem.CanNextStepPage ? LaunchpadColors.GreenBright : LaunchpadColors.Off);
        }
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
