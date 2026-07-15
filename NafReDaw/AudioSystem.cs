using NafAudio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NafReDaw;

public static class AudioSystem
{
    public delegate void PlayOneShotFinishedDelegate();
    public static bool AddSample(byte note, string sourceFile)
    {
        var sourcePath = Path.IsPathRooted(sourceFile)
            ? sourceFile
            : Path.Combine(Directory.GetCurrentDirectory(), sourceFile);

        if (!File.Exists(sourcePath))
        {
            App.Output($"File not found '{sourcePath}'.");
            return false;
        }

        var samplesFolder = GetSamplesFolder();

        var destFileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(samplesFolder, destFileName);

        try
        {
            File.Copy(sourcePath, destPath, overwrite: true);
        }
        catch (Exception ex)
        {
            App.Output($"Failed to copy sample to '{destPath}': {ex.Message}");
            return false;
        }

        AssignSampleFromPath(note, destPath);
        App.Project.ChangesMade = true;        
        App.Output($"Assigned '{destFileName}' to note 0x{note:X2}.");
        
        return true;
    }

    public static bool RemoveSample(byte note)
    {
        var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == note);
        if (sample is null)
        {
            App.Output($"No sample assigned to note 0x{note:X2}.");
            return false;
        }

        App.Project.LoadedSamples.Remove(sample);
        App.Project.ChangesMade = true;        
        App.Output($"Removed sample from note 0x{note:X2}.");

        return true;
    }

    public static string GetSamplesFolder()
    {
        var samplesFolder = Path.Combine(Directory.GetCurrentDirectory(), App.Project.SamplesFolder);
        Directory.CreateDirectory(samplesFolder);

        return samplesFolder;
    }

    public static void LoadSamplesIntoMemory()
    {
        var baseFolder = GetSamplesFolder();
        foreach (var sample in App.Project.LoadedSamples)
        {
            sample.InMemorySample = null;
            try
            {
                var fullPath = Path.Combine(baseFolder, sample.FileName);
                if (!File.Exists(fullPath))
                {
                    App.Output($"Sample file not found '{fullPath}'.");
                    continue;
                }

                sample.InMemorySample = AsioSampleEngine.LoadSample(fullPath);
            }
            catch (Exception ex)
            {
                App.Output($"Failed to load sample '{sample.FileName}': {ex.Message}");
            }
        }
    }

    public static bool AssignSampleFromPath(byte note, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == note);
        if (sample is null)
        {
            sample = new LoadedSample();
            App.Project.LoadedSamples.Add(sample);
        }

        sample.Note = note;
        sample.FileName = fileName;
        sample.StartSample = 0;

        try
        {
            sample.InMemorySample = AsioSampleEngine.LoadSample(filePath);
            sample.EndSample = sample.InMemorySample.SampleCount;
            return true;
        }
        catch (Exception ex)
        {
            App.Output($"Failed to load sample '{fileName}': {ex.Message}");
            sample.InMemorySample = null;
            sample.EndSample = 0;
            return false;
        }
    }

    public static void TrimSample(int? startMilliSeconds = null, int? endMilliSeconds = null)
    {
        if (App.CurrentlySelectedNote == -1)
        {
            return;
        }

        var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == App.CurrentlySelectedNote);
        if (sample?.InMemorySample is null)
        {
            return;
        }

        int sampleRate = sample.InMemorySample.WaveFormat.SampleRate;
        int totalSamples = sample.InMemorySample.SampleCount;
        int endSample = sample.EndSample > 0 ? sample.EndSample : totalSamples;
        bool changed = false;

        if (startMilliSeconds is int startMs)
        {
            int delta = (int)((long)startMs * sampleRate / 1000);
            int newStart = Math.Clamp(sample.StartSample + delta, 0, endSample - 1);
            if (newStart != sample.StartSample)
            {
                sample.StartSample = newStart;
                changed = true;
            }
        }

        if (endMilliSeconds is int endMs)
        {
            int delta = (int)((long)endMs * sampleRate / 1000);
            int newEnd = Math.Clamp(endSample + delta, sample.StartSample + 1, totalSamples);
            if (newEnd != endSample)
            {
                sample.EndSample = newEnd;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        App.Project.ChangesMade = true;

        App.Debug($"Trim note 0x{App.CurrentlySelectedNote:X2}: start={sample.StartSample}, end={sample.EndSample} ({totalSamples} samples)");
    }

    public static bool ToggleLoop()
    {
        if (App.CurrentlySelectedNote == -1)
        {
            return false;
        }

        var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == App.CurrentlySelectedNote);
        if (sample is null)
        {
            return false;
        }

        sample.Loop = !sample.Loop;
        App.Project.ChangesMade = true;

        App.Debug($"Loop note 0x{App.CurrentlySelectedNote:X2}: {sample.Loop}");
        return true;
    }

    public static bool PlayLoadedSample(LoadedSample sample, PlayOneShotFinishedDelegate onFinished)
    {
        if (sample.InMemorySample is null)
        {
            return false;
        }

        try
        {
            if (App.CurrentlyPlayingSampleHandle != -1)
            {
                App.AudioEngine.StopPlayback(App.CurrentlyPlayingSampleHandle);
                App.CurrentlyPlayingSampleHandle = -1;
                App.CurrentlyPlayingNote = -1;
            }

            App.CurrentlyPlayingNote = sample.Note;
            App.CurrentlyPlayingSampleHandle = App.AudioEngine.PlayOneShot(sample.InMemorySample, sample.StartSample, sample.EndSample, sample.Loop, () =>
            {
                App.CurrentlyPlayingSampleHandle = -1;
                App.CurrentlyPlayingNote = -1;
                onFinished();
            });

            if (App.CurrentlyPlayingSampleHandle == -1)
            {
                App.CurrentlyPlayingNote = -1;
            }

            return true;
        }
        catch (Exception ex)
        {
            App.CurrentlyPlayingSampleHandle = -1;
            App.CurrentlyPlayingNote = -1;
            App.Output($"Failed to play sample '{sample.FileName}': {ex.Message}");

            return true;
        }
    }


    public static bool StartSamplingRecording()
    {
        try
        {
            App.AudioEngine.StartRecording();
            App.Output($"Recording for note 0x{App.CurrentlySelectedNote:X2}...");
        }
        catch (Exception ex)
        {
            App.Output($"Failed to start recording: {ex.Message}");
            return false;
        }

        return true;
    }

    public static void StopSamplingRecording()
    {
        var samplesFolder = AudioSystem.GetSamplesFolder();
        var destPath = GetUniqueRecordingPath(samplesFolder);
        var note = (byte)App.CurrentlySelectedNote;

        try
        {
            App.AudioEngine.SaveRecording(destPath);
        }
        catch (InvalidOperationException)
        {
            App.Output("No audio recorded.");
            return;
        }
        catch (Exception ex)
        {
            App.Output($"Failed to save recording: {ex.Message}");
            return;
        }

        AudioSystem.AssignSampleFromPath(note, destPath);
        var sample = App.Project.LoadedSamples.First(s => s.Note == note);
        sample.Loop = false;
        App.CurrentlySelectedNote = -1;
        App.Project.ChangesMade = true;
        App.Output($"Recorded '{Path.GetFileName(destPath)}' to note 0x{note:X2}.");
    }

    public static void CancelSamplingRecording()
    {
        App.AudioEngine.StopRecording();
        App.Output("Recording cancelled.");
    }

    private static string GetUniqueRecordingPath(string samplesFolder)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var path = Path.Combine(samplesFolder, $"recording_{timestamp}.wav");
        for (int i = 1; File.Exists(path); i++)
        {
            path = Path.Combine(samplesFolder, $"recording_{timestamp}_{i}.wav");
        }

        return path;
    }

    public static void ApplyAudioDevice()
    {
        App.AudioEngine.Stop();
        App.AudioEngine.PlaybackDeviceId = App.Project.AudioPlaybackDeviceId;
        App.AudioEngine.RecordingDeviceId = App.Project.AudioRecordingDeviceId;

        var drivers = AsioSampleEngine.GetDrivers();
        if (drivers.Count == 0)
        {
            App.Output("No ASIO drivers found. Audio disabled — use 'audio' to list drivers when one is installed.");
            return;
        }

        try
        {
            App.AudioEngine.StartPlayback();
            App.Output($"ASIO playback started (playback '{App.Project.AudioPlaybackDeviceId}', recording '{App.Project.AudioRecordingDeviceId}').");
        }
        catch (Exception ex)
        {
            App.Output($"Failed to start ASIO audio: {ex.Message}");
            App.Output("Audio disabled — connect your interface and run 'audio' to list available audio drivers.");
        }
    }

}

