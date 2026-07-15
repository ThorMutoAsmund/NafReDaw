using NafAudio;
using NafMidi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NafReDaw;

public static class MidiSystem
{
    public delegate void NotePadPressedDelegate(LaunchpadPadEventArgs e);
    public delegate void NotePadReleasedDelegate(LaunchpadPadEventArgs e);
    public delegate void SideButtonDelegate(LaunchpadButtonEventArgs e);
    public static void CreateLaunchPadDevice(
        NotePadPressedDelegate notePadPressedDelegate,
        NotePadReleasedDelegate notePadReleasedDelegate,
        SideButtonDelegate sideButtonDelegate,
        int inputDeviceIndex = 0, int outputDeviceIndex = 0)
    {
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

}

