namespace NafReDaw;

using System.Text.Json.Serialization;

public class DawProject
{
    public int MidiInputDeviceIndex { get; set; } = 0;
    public int MidiOutputDeviceIndex { get; set; } = 0;
    public string AudioPlaybackDeviceId { get; set; } = "-1";
    public string AudioRecordingDeviceId { get; set; } = "-1";
    public string SamplesFolder { get; set; } = "samples";
    public List<LoadedSample> LoadedSamples { get; set; } = [];

    [JsonIgnore]
    public bool ChangesMade { get; set; }
}
