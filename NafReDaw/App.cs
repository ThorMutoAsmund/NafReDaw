using NafAudio;
using NafMidi;
using System.Diagnostics;

namespace NafReDaw;

public static class App
{
    public const string ProjectFileExtension = ".nafdaw";
    public const string DebugProjcetFolder = "C:\\Users\\thora\\Google Drive\\Music\\NafDaw\\Debug";
    public const int LongTrimSeconds = 100;
    public const int ShortTrimSeconds = 10;
    public const float LongVolumeStep = 0.1f;
    public const float ShortVolumeStep = 0.01f;

    public static DawProject Project { get; set; } = new DawProject();
    public static LaunchpadDevice Launchpad { get; set; } = null!;
    public static DawMode DawMode { get; set; } = DawMode.Play;
    public static SubMode SubMode { get; set; } = SubMode.Playing;
    public static EditTool EditTool { get; set; } = EditTool.None;
    public static AsioSampleEngine AudioEngine { get; set; } = new AsioSampleEngine();
    public static int CurrentlyPlayingSampleHandle { get; set; } = -1;
    public static int CurrentlyPlayingNote { get; set; } = -1;
    public static int CurrentlySelectedNote { get; set; } = -1;
    public static int ActivePatternIndex { get; set; }
    /// <summary>Which 8-column window of the 64-step pattern is shown on the Launchpad (0–7).</summary>
    public static int ArrangeStepPage { get; set; }
    /// <summary>Note used to paint steps in Arrange mode (-1 = none).</summary>
    public static int ArrangePaintNote { get; set; } = -1;
    public static bool IsShiftHeld { get; set; }
    public static bool IsRecordHeld { get; set; }
    /// <summary>When true, sample recordings are saved as mono (downmixed from stereo input).</summary>
    public static bool RecordMono { get; set; }
    public static bool ChangesMade { get; set; }

    public static void Output(string message)
    {
        Console.WriteLine(message);
    }

    public static void Debug(string message)
    {
        if (Debugger.IsAttached)
        {
            Output(message);
        }
    }
}
