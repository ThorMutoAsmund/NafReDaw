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

    public bool PlayBackwards { get; set; }

    public float Volume { get; set; } = 1f;

    /// <summary>Shared group id for exclusive play (-1 = not in a group).</summary>
    public int GroupId { get; set; } = -1;

    [JsonIgnore]
    public InMemorySample? InMemorySample { get; set; }
}

