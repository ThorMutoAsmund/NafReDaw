// LaunchpadMidi.cs — portable Novation Launchpad MIDI send/receive for NAudio.
//
// Drop this single file into a new project. It is NOT referenced by NafReZampler.
//
// Requirements:
//   - Target: net8.0-windows (or any Windows target with NAudio MIDI support)
//   - NuGet:  NAudio 2.2.1 or higher
//
// Protocol: Launchpad Classic / Mk1 "programmer" mode — Note On velocity selects LED color,
// side buttons send/receive Control Change. No SysEx.
//
// Quick start:
//
//   using NafMidi;
//
//   using var launchpad = new LaunchpadDevice();
//   launchpad.PadPressed += (_, e) =>
//       Console.WriteLine($"Pad {e.Row},{e.Column} note 0x{e.NoteNumber:X2}");
//   launchpad.Start(inputDeviceIndex: 0, outputDeviceIndex: 0);
//
//   launchpad.SetPad(0, 0, LaunchpadColors.Green);   // top-left pad
//   launchpad.SetSideButton(LaunchpadLayout.ClockButtonCc, LaunchpadColors.Amber);
//   launchpad.ClearGrid();
//   launchpad.Stop();
//
// List devices:
//
//   foreach (var d in LaunchpadDevice.ListInputDevices())
//       Console.WriteLine($"{d.Index}: {d.Name}");

using NAudio.Midi;

namespace NafMidi;

public sealed record MidiDeviceInfo(int Index, string Name);

/// <summary>
/// 8×8 grid note numbers and side-button CC mappings for Novation Launchpad (Classic layout).
/// Visual row 0 is the top row of the pad grid; column 0 is the left column.
/// </summary>
public static class LaunchpadLayout
{
    public const int GridRows = 8;
    public const int GridColumns = 8;
    public const int DefaultChannel = 0;

    /// <summary>Bottom-row, left-pad note number (Launchpad note map origin).</summary>
    public const int GridFirstNote = 0x0B;

    /// <summary>Distance in note numbers between grid rows.</summary>
    public const int GridRowStride = 0x0A;

    public const int GridLastNote = 0x58;

    public const int UndoButtonCc = 0x3C;
    public const int ClockButtonCc = 0x46;
    public const int SessionButtonCc = 0x5F;
    public const int NoteButtonCc = 0x60;
    public const int DeviceButtonCc = 0x61;
    public const int UserButtonCc = 0x62;

    /// <summary>CC for the round side button beside grid row <paramref name="row"/> (0–7).</summary>
    public static int RowButtonCc(int row) => 19 + row * 10;

    public static bool IsGridNote(int note) =>
        note >= GridFirstNote && note <= GridLastNote && (note - GridFirstNote) % GridRowStride <= 7;

    public static int NoteFromGrid(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, GridRows);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, GridColumns);
        return GridFirstNote + (GridRows - 1 - row) * GridRowStride + column;
    }

    public static (int Row, int Column)? GridFromNote(int note)
    {
        if (!IsGridNote(note))
        {
            return null;
        }

        int offset = note - GridFirstNote;
        int midiRow = offset / GridRowStride;
        int row = GridRows - 1 - midiRow;
        int column = offset % GridRowStride;
        return (row, column);
    }

    /// <summary>True for CC 19, 29, …, 89 (Launchpad row side buttons).</summary>
    public static bool IsRowButtonCc(int controller) =>
        controller >= 19 && controller <= 89 && (controller - 19) % 10 == 0;

    public static int RowFromButtonCc(int controller) => (controller - 19) / 10;
}

/// <summary>
/// Common Launchpad LED color indices (Note On velocity values).
/// The Launchpad maps velocity 0–127 to its fixed palette.
/// </summary>
public static class LaunchpadColors
{
    public const byte Off = 0;
    public const byte DimWhite = 1;
    public const byte Red = 5;
    public const byte Amber = 9;
    public const byte Yellow = 11;
    public const byte YellowBright = 13;
    public const byte Green = 15;
    public const byte GreenBright = 21;
    public const byte Cyan = 37;
    public const byte Blue = 45;
    public const byte Purple = 49;
    public const byte Pink = 53;
    public const byte Echo = 44;
}

/// <summary>Semantic pad states mapped to Launchpad color velocities.</summary>
public enum LaunchpadPadState
{
    Off,
    Loaded,
    Waiting,
    Playing,
    Stopping,
    WaitingToRecord,
    Recording,
    EndingRecordingOff,
    EndingRecordingPlay,
    Echo
}


public sealed class LaunchpadPadEventArgs : EventArgs
{
    public int NoteNumber { get; }
    public byte Velocity { get; }
    public int Row { get; }
    public int Column { get; }

    public LaunchpadPadEventArgs(int noteNumber, byte velocity, int row, int column)
    {
        NoteNumber = noteNumber;
        Velocity = velocity;
        Row = row;
        Column = column;
    }
}

public sealed class LaunchpadButtonEventArgs : EventArgs
{
    public int ControllerNumber { get; }
    public byte Value { get; }
    public int? Row { get; }

    public LaunchpadButtonEventArgs(int controllerNumber, byte value, int? row)
    {
        ControllerNumber = controllerNumber;
        Value = value;
        Row = row;
    }
}



/// <summary>
/// Sends a temporary control-change value, then resets it after a delay.
/// If pulsed again before reset, the previous reset is canceled.
/// </summary>
public sealed class LaunchpadControlChangePulse
{
    private readonly object _sync = new();
    private CancellationTokenSource? _resetCts;

    public void Pulse(Action<byte> sendValue, byte onValue, byte offValue = 0, int delayMilliseconds = 80)
    {
        sendValue(onValue);

        CancellationTokenSource cts;
        lock (_sync)
        {
            _resetCts?.Cancel();
            _resetCts?.Dispose();
            _resetCts = new CancellationTokenSource();
            cts = _resetCts;
        }

        _ = ResetAsync(sendValue, offValue, delayMilliseconds, cts.Token);
    }

    private static async Task ResetAsync(Action<byte> sendValue, byte finalValue, int delayMilliseconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delayMilliseconds, cancellationToken);
            sendValue(finalValue);
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer pulse.
        }
    }
}



/// <summary>
/// Sends and receives MIDI for a Novation Launchpad via NAudio.
/// </summary>
public sealed class LaunchpadDevice : IDisposable
{
    private MidiIn? _midiIn;
    private MidiOut? _midiOut;
    private string? _inputDeviceDescription;
    private string? _outputDeviceDescription;
    private readonly LaunchpadControlChangePulse _pulse = new();
    private readonly HashSet<int> _loadedPads = new();

    public event EventHandler<LaunchpadPadEventArgs>? PadPressed;
    public event EventHandler<LaunchpadPadEventArgs>? PadReleased;
    public event EventHandler<LaunchpadButtonEventArgs>? SideButtonPressed;
    public event EventHandler<LaunchpadButtonEventArgs>? SideButtonReleased;
    public event EventHandler<MidiInMessageEventArgs>? RawMessageReceived;
    public event EventHandler<MidiInMessageEventArgs>? ErrorReceived;

    public bool IsRunning => _midiIn != null || _midiOut != null;
    public string? InputDeviceDescription => _inputDeviceDescription;
    public string? OutputDeviceDescription => _outputDeviceDescription;

    public static IReadOnlyList<MidiDeviceInfo> ListInputDevices()
    {
        var list = new List<MidiDeviceInfo>(MidiIn.NumberOfDevices);
        for (int i = 0; i < MidiIn.NumberOfDevices; i++)
        {
            list.Add(new MidiDeviceInfo(i, MidiIn.DeviceInfo(i).ProductName?.Trim() ?? $"Device {i}"));
        }

        return list;
    }

    public static IReadOnlyList<MidiDeviceInfo> ListOutputDevices()
    {
        var list = new List<MidiDeviceInfo>(MidiOut.NumberOfDevices);
        for (int i = 0; i < MidiOut.NumberOfDevices; i++)
        {
            list.Add(new MidiDeviceInfo(i, MidiOut.DeviceInfo(i).ProductName?.Trim() ?? $"Device {i}"));
        }

        return list;
    }

    /// <summary>Returns the first input/output device whose name contains "Launchpad" (case-insensitive), or null.</summary>
    public static (MidiDeviceInfo? Input, MidiDeviceInfo? Output) FindLaunchpadDevices()
    {
        MidiDeviceInfo? input = null;
        foreach (var device in ListInputDevices())
        {
            if (device.Name.Contains("Launchpad", StringComparison.OrdinalIgnoreCase))
            {
                input = device;
                break;
            }
        }

        MidiDeviceInfo? output = null;
        foreach (var device in ListOutputDevices())
        {
            if (device.Name.Contains("Launchpad", StringComparison.OrdinalIgnoreCase))
            {
                output = device;
                break;
            }
        }

        return (input, output);
    }

    /// <summary>
    /// Opens MIDI input and output devices. Pass -1 to skip input or output.
    /// </summary>
    public void Start(int inputDeviceIndex = 0, int outputDeviceIndex = 0)
    {
        Stop();

        if (inputDeviceIndex >= 0 && inputDeviceIndex < MidiIn.NumberOfDevices)
        {
            _midiIn = new MidiIn(inputDeviceIndex);
            _midiIn.MessageReceived += MidiIn_MessageReceived;
            _midiIn.ErrorReceived += (_, e) => ErrorReceived?.Invoke(this, e);
            _midiIn.Start();
            _inputDeviceDescription = $"MIDI In {inputDeviceIndex}: {MidiIn.DeviceInfo(inputDeviceIndex).ProductName}";
        }

        if (outputDeviceIndex >= 0 && outputDeviceIndex < MidiOut.NumberOfDevices)
        {
            _midiOut = new MidiOut(outputDeviceIndex);
            _outputDeviceDescription = $"MIDI Out {outputDeviceIndex}: {MidiOut.DeviceInfo(outputDeviceIndex).ProductName}";
        }
    }

    /// <summary>Starts using devices returned by <see cref="FindLaunchpadDevices"/>.</summary>
    public bool StartLaunchpad()
    {
        var (input, output) = FindLaunchpadDevices();
        if (input == null || output == null)
        {
            return false;
        }

        Start(input.Index, output.Index);
        return true;
    }

    public void Stop()
    {
        ClearAll();
        _midiIn?.Stop();
        _midiIn?.Dispose();
        _midiIn = null;
        _midiOut?.Dispose();
        _midiOut = null;
        _inputDeviceDescription = null;
        _outputDeviceDescription = null;
        _loadedPads.Clear();
    }

    public void Dispose() => Stop();

    /// <summary>Marks a pad as having content; <see cref="LaunchpadPadState.Off"/> shows dim white instead of black.</summary>
    public void SetPadLoaded(int row, int column, bool loaded = true)
    {
        int note = LaunchpadLayout.NoteFromGrid(row, column);
        if (loaded)
        {
            _loadedPads.Add(note);
        }
        else
        {
            _loadedPads.Remove(note);
        }
    }

    public void SetPad(int row, int column, byte colorVelocity) =>
        SendNoteOn(LaunchpadLayout.NoteFromGrid(row, column), colorVelocity);

    public void SetPad(int noteNumber, byte colorVelocity) => SendNoteOn(noteNumber, colorVelocity);

    public void SetPadState(int row, int column, LaunchpadPadState state)
    {
        int note = LaunchpadLayout.NoteFromGrid(row, column);
        SendNoteOn(note, ColorForPadState(note, state));
    }

    public void SetPadState(int noteNumber, LaunchpadPadState state) =>
        SendNoteOn(noteNumber, ColorForPadState(noteNumber, state));

    public void ClearPad(int row, int column) => SetPad(row, column, LaunchpadColors.Off);

    public void ClearGrid()
    {
        for (int row = 0; row < LaunchpadLayout.GridRows; row++)
        {
            for (int col = 0; col < LaunchpadLayout.GridColumns; col++)
            {
                ClearPad(row, col);
            }
        }
    }

    public void SetSideButton(int controllerNumber, byte value) =>
        SendControlChange(controllerNumber, value);

    public void SetRowButton(int row, byte value) =>
        SetSideButton(LaunchpadLayout.RowButtonCc(row), value);

    public void PulseSideButton(int controllerNumber, byte onValue, byte offValue = 0, int delayMilliseconds = 80)
    {
        _pulse.Pulse(value => SendControlChange(controllerNumber, value), onValue, offValue, delayMilliseconds);
    }

    public void PulseClockButton(bool accented = false) =>
        PulseSideButton(LaunchpadLayout.ClockButtonCc, accented ? LaunchpadColors.GreenBright : LaunchpadColors.Amber);

    /// <summary>Sends note-off (velocity 0) for all notes and side-button CCs.</summary>
    public void ClearAll()
    {
        if (_midiOut == null)
        {
            return;
        }

        for (int note = 0; note < 128; note++)
        {
            SendNoteOn(note, 0);
        }

        SendControlChange(LaunchpadLayout.UndoButtonCc, 0);
        SendControlChange(LaunchpadLayout.ClockButtonCc, 0);
        for (int row = 0; row < LaunchpadLayout.GridRows; row++)
        {
            SetRowButton(row, 0);
        }
    }

    public void SendNoteOn(int note, byte velocity, int channel = LaunchpadLayout.DefaultChannel)
    {
        if (_midiOut == null)
        {
            return;
        }

        int message = (0x90 + channel) | (note << 8) | (velocity << 16);
        _midiOut.Send(message);
    }

    public void SendControlChange(int controllerNumber, byte value, int channel = LaunchpadLayout.DefaultChannel)
    {
        if (_midiOut == null)
        {
            return;
        }

        int message = (0xB0 + channel)
            | (Math.Clamp(controllerNumber, 0, 127) << 8)
            | (Math.Clamp((int)value, 0, 127) << 16);
        _midiOut.Send(message);
    }

    public void SendRaw(int rawMessage)
    {
        _midiOut?.Send(rawMessage);
    }

    private byte ColorForPadState(int note, LaunchpadPadState state)
    {
        if (state == LaunchpadPadState.Off && _loadedPads.Contains(note))
        {
            return LaunchpadColors.DimWhite;
        }

        return state switch
        {
            LaunchpadPadState.Off => LaunchpadColors.Off,
            LaunchpadPadState.Loaded => LaunchpadColors.DimWhite,
            LaunchpadPadState.Waiting => LaunchpadColors.YellowBright,
            LaunchpadPadState.Playing => LaunchpadColors.GreenBright,
            LaunchpadPadState.Stopping => LaunchpadColors.Amber,
            LaunchpadPadState.WaitingToRecord => LaunchpadColors.Yellow,
            LaunchpadPadState.Recording => LaunchpadColors.Red,
            LaunchpadPadState.EndingRecordingOff => LaunchpadColors.Yellow,
            LaunchpadPadState.EndingRecordingPlay => LaunchpadColors.Green,
            LaunchpadPadState.Echo => LaunchpadColors.Echo,
            _ => LaunchpadColors.Off
        };
    }

    private void MidiIn_MessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        RawMessageReceived?.Invoke(this, e);

        switch (e.MidiEvent)
        {
            case NoteOnEvent noteOn:
                HandleNoteEvent(noteOn.NoteNumber, (byte)noteOn.Velocity);
                break;
            case NoteEvent noteEvent when e.MidiEvent.CommandCode == MidiCommandCode.NoteOff:
                HandleNoteEvent(noteEvent.NoteNumber, 0);
                break;
            case ControlChangeEvent controlChange:
                HandleControlChange((int)controlChange.Controller, (byte)controlChange.ControllerValue);
                break;
        }
    }

    private void HandleNoteEvent(int note, byte velocity)
    {
        var grid = LaunchpadLayout.GridFromNote(note);
        if (grid == null)
        {
            return;
        }

        var (row, column) = grid.Value;
        var args = new LaunchpadPadEventArgs(note, velocity, row, column);
        if (velocity > 0)
        {
            PadPressed?.Invoke(this, args);
        }
        else
        {
            PadReleased?.Invoke(this, args);
        }
    }

    private void HandleControlChange(int controller, byte value)
    {
        int? row = LaunchpadLayout.IsRowButtonCc(controller)
            ? LaunchpadLayout.RowFromButtonCc(controller)
            : null;

        var args = new LaunchpadButtonEventArgs(controller, value, row);
        if (value > 0)
        {
            SideButtonPressed?.Invoke(this, args);
        }
        else
        {
            SideButtonReleased?.Invoke(this, args);
        }
    }
}

