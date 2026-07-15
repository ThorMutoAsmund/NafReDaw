using NafMidi;

namespace NafReDaw;

public static class MidiSystem
{
    public delegate void NotePadPressedDelegate(LaunchpadPadEventArgs e);
    public delegate void NotePadReleasedDelegate(LaunchpadPadEventArgs e);
    public delegate void SideButtonDelegate(LaunchpadButtonEventArgs e);

    private static CancellationTokenSource? _vuMeterCts;
    private const int VuMeterIntervalMilliseconds = 50;

    public static void CreateLaunchPadDevice(
        NotePadPressedDelegate notePadPressedDelegate,
        NotePadReleasedDelegate notePadReleasedDelegate,
        SideButtonDelegate sideButtonDelegate,
        int inputDeviceIndex = 0, int outputDeviceIndex = 0)
    {
        StopVuMeter();

        if (App.Launchpad != null)
        {
            App.Launchpad.Stop();
            App.Launchpad.Dispose();
        }

        App.Launchpad = new LaunchpadDevice();

        App.Launchpad.PadPressed += (_, e) => notePadPressedDelegate(e);
        App.Launchpad.PadReleased += (_, e) => notePadReleasedDelegate(e);
        App.Launchpad.SideButtonPressed += (_, e) => sideButtonDelegate(e);

        App.Launchpad.Start(inputDeviceIndex: inputDeviceIndex, outputDeviceIndex: outputDeviceIndex);

        var inputName = LaunchpadDevice.ListInputDevices().FirstOrDefault(d => d.Index == inputDeviceIndex)?.Name ?? "<none>";
        var outputName = LaunchpadDevice.ListOutputDevices().FirstOrDefault(d => d.Index == outputDeviceIndex)?.Name ?? "<none>";
        App.Output($"MIDI devices connected (input '{inputName}', output '{outputName}')");
    }

    public static void StartVuMeter()
    {
        StopVuMeter();
        _vuMeterCts = new CancellationTokenSource();
        var cts = _vuMeterCts;
        _ = RunVuMeterAsync(cts.Token);
    }

    public static void StopVuMeter()
    {
        _vuMeterCts?.Cancel();
        _vuMeterCts?.Dispose();
        _vuMeterCts = null;
        ClearVuMeter();
    }

    public static void UpdateVuMeter()
    {
        if (App.SubMode != SubMode.Recording || App.Launchpad is null)
        {
            return;
        }

        var (left, right) = App.AudioEngine.GetInputLevels();
        var level = Math.Sqrt(Math.Max(left, right));

        for (var segment = 0; segment < LaunchpadLayout.GridRows; segment++)
        {
            var threshold = (segment + 1) / (float)LaunchpadLayout.GridRows;
            var color = level >= threshold ? VuSegmentColor(segment) : LaunchpadColors.Off;
            App.Launchpad.SetSideButton(LaunchpadLayout.RowButtonCc(segment), color);
        }
    }

    private static async Task RunVuMeterAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (App.SubMode != SubMode.Recording)
                {
                    break;
                }

                UpdateVuMeter();
                await Task.Delay(VuMeterIntervalMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void ClearVuMeter()
    {
        if (App.Launchpad is null)
        {
            return;
        }

        for (var row = 0; row < LaunchpadLayout.GridRows; row++)
        {
            App.Launchpad.SetSideButton(LaunchpadLayout.RowButtonCc(row), LaunchpadColors.Off);
        }
    }

    private static byte VuSegmentColor(int segment)
    {
        if (segment <= 4)
        {
            return LaunchpadColors.GreenBright;
        }

        if (segment <= 6)
        {
            return LaunchpadColors.Yellow;
        }

        return LaunchpadColors.Red;
    }
}
