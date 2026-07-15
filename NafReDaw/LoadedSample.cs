using NafAudio;
using System.Text.Json.Serialization;

namespace NafReDaw;

public class LoadedSample
{
    public string FileName { get; set; } = "";

    public byte Note { get; set; }

    public int StartSample { get; set; }

    public int EndSample { get; set; }

    public bool Loop { get; set; }

    [JsonIgnore]
    public InMemorySample? InMemorySample { get; set; }
}

