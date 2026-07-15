namespace NafReDaw;

public class DawProject
{
    public int MidiInputDeviceIndex { get; set; } = 0;
    public int MidiOutputDeviceIndex { get; set; } = 0;
    public string AudioPlaybackDeviceId { get; set; } = "-1";
    public string AudioRecordingDeviceId { get; set; } = "-1";
    public string SamplesFolder { get; set; } = "samples";
    public List<LoadedSample> LoadedSamples { get; set; } = [];
    public Arrangement Arrangement { get; set; } = new();
}
