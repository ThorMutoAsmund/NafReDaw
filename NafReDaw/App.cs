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

    public static DawProject Project { get; set; } = new DawProject();
    public static LaunchpadDevice Launchpad { get; set; } = null!;
    public static DawMode DawMode { get; set; } = DawMode.Play;
    public static SubMode SubMode { get; set; } = SubMode.Play;
    public static EditTool EditTool { get; set; } = EditTool.None;
    public static AsioSampleEngine AudioEngine { get; set; } = new AsioSampleEngine();
    public static int CurrentlyPlayingSampleHandle { get; set; } = -1;
    public static int CurrentlyPlayingNote { get; set; } = -1;
    public static int CurrentlySelectedNote { get; set; } = -1;

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
