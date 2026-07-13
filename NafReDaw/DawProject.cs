namespace NafReDaw;

using System.Text.Json.Serialization;

public class DawProject
{
    public int MidiInputDeviceIndex { get; set; } = 0;
    public int MidiOutputDeviceIndex { get; set; } = 0;

    [JsonIgnore]
    public bool ChangesMade { get; set; }
}
