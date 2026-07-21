using NafAudio;

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
        App.ChangesMade = true;        
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

        var oldGroupId = sample.GroupId;
        App.Project.LoadedSamples.Remove(sample);
        if (oldGroupId != -1)
        {
            DissolveUndersizedGroup(oldGroupId);
        }

        App.ChangesMade = true;
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

    public static bool TrimSample(int? startMilliSeconds = null, int? endMilliSeconds = null)
    {
        if (App.CurrentlySelectedNote == -1)
        {
            return false;
        }

        var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == App.CurrentlySelectedNote);
        if (sample?.InMemorySample is null)
        {
            return false;
        }

        var sampleRate = sample.InMemorySample.WaveFormat.SampleRate;
        var totalSamples = sample.InMemorySample.SampleCount;
        var endSample = sample.EndSample > 0 ? sample.EndSample : totalSamples;
        bool changed;

        if (startMilliSeconds is int startMs)
        {
            var delta = (int)((long)startMs * sampleRate / 1000);
            var newStart = Math.Clamp(sample.StartSample + delta, 0, endSample - 1);
            changed = newStart != sample.StartSample;
            if (changed)
            {
                sample.StartSample = newStart;
            }
        }
        else if (endMilliSeconds is int endMs)
        {
            var delta = (int)((long)endMs * sampleRate / 1000);
            var newEnd = Math.Clamp(endSample + delta, sample.StartSample + 1, totalSamples);
            changed = newEnd != endSample;
            if (changed)
            {
                sample.EndSample = newEnd;
            }
        }
        else
        {
            return false;
        }

        if (!changed)
        {
            return false;
        }

        App.ChangesMade = true;

        App.Debug($"Trim note 0x{App.CurrentlySelectedNote:X2}: start={sample.StartSample}, end={sample.EndSample} ({totalSamples} samples)");

        return true;
    }

    /// <summary>
    /// Sets Start/End to the first and last frames louder than <paramref name="threshold"/>.
    /// </summary>
    public static bool TrimSilence(float threshold = 0.002f)
    {
        if (App.CurrentlySelectedNote == -1)
        {
            return false;
        }

        var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == App.CurrentlySelectedNote);
        if (sample?.InMemorySample is null)
        {
            return false;
        }

        var samples = sample.InMemorySample.Samples;
        var channels = Math.Max(1, sample.InMemorySample.WaveFormat.Channels);
        var frameCount = samples.Length / channels;
        if (frameCount < 1)
        {
            return false;
        }

        var firstFrame = -1;
        for (var frame = 0; frame < frameCount; frame++)
        {
            if (FramePeak(samples, frame, channels) > threshold)
            {
                firstFrame = frame;
                break;
            }
        }

        if (firstFrame < 0)
        {
            App.Output("Trim silence: sample is entirely below threshold.");
            return false;
        }

        var lastFrame = firstFrame;
        for (var frame = frameCount - 1; frame >= firstFrame; frame--)
        {
            if (FramePeak(samples, frame, channels) > threshold)
            {
                lastFrame = frame;
                break;
            }
        }

        var newStart = firstFrame * channels;
        var newEnd = (lastFrame + 1) * channels;
        if (newStart == sample.StartSample && newEnd == sample.EndSample)
        {
            return false;
        }

        sample.StartSample = newStart;
        sample.EndSample = newEnd;
        App.ChangesMade = true;

        App.Output($"Trimmed silence note 0x{App.CurrentlySelectedNote:X2}: start={newStart}, end={newEnd}");
        return true;
    }

    private static float FramePeak(float[] samples, int frame, int channels)
    {
        var peak = 0f;
        var offset = frame * channels;
        for (var ch = 0; ch < channels; ch++)
        {
            var value = Math.Abs(samples[offset + ch]);
            if (value > peak)
            {
                peak = value;
            }
        }

        return peak;
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
        App.ChangesMade = true;

        App.Debug($"Loop note 0x{App.CurrentlySelectedNote:X2}: {sample.Loop}");
        return true;
    }

    public static bool TogglePlayBackwards()
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

        sample.PlayBackwards = !sample.PlayBackwards;
        App.ChangesMade = true;

        App.Debug($"PlayBackwards note 0x{App.CurrentlySelectedNote:X2}: {sample.PlayBackwards}");
        return true;
    }

    public static bool AdjustSampleVolume(float delta)
    {
        if (App.CurrentlySelectedNote == -1)
        {
            return false;
        }

        var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == App.CurrentlySelectedNote);
        if (sample?.InMemorySample is null)
        {
            return false;
        }

        var newVolume = Math.Clamp(sample.Volume + delta, 0f, 1f);
        if (newVolume == sample.Volume)
        {
            return false;
        }

        sample.Volume = newVolume;
        App.ChangesMade = true;

        App.Debug($"Volume note 0x{App.CurrentlySelectedNote:X2}: {sample.Volume:F2}");

        return true;
    }

    public static bool TryGetPlayVoice(int note, out int handle)
    {
        return App.ActivePlayVoices.TryGetValue(note, out handle);
    }

    public static void RegisterPlayVoice(int note, int handle)
    {
        App.ActivePlayVoices[note] = handle;
    }

    public static void ClearPlayVoice(int note)
    {
        App.ActivePlayVoices.Remove(note);
    }

    public static void ClearPlayVoiceIfHandle(int note, int handle)
    {
        if (App.ActivePlayVoices.TryGetValue(note, out var current) && current == handle)
        {
            App.ActivePlayVoices.Remove(note);
        }
    }

    public static void ClearAllPlayVoices()
    {
        App.ActivePlayVoices.Clear();
    }

    public static void TogglePendingGroupNote(int note)
    {
        if (!App.PendingGroupNotes.Add(note))
        {
            App.PendingGroupNotes.Remove(note);
        }
    }

    public static void CommitPendingGroup()
    {
        var notes = App.PendingGroupNotes.ToList();
        App.PendingGroupNotes.Clear();

        if (notes.Count == 0)
        {
            return;
        }

        if (notes.Count == 1)
        {
            var only = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == notes[0]);
            if (only is null || only.GroupId == -1)
            {
                return;
            }

            var oldGroupId = only.GroupId;
            only.GroupId = -1;
            DissolveUndersizedGroup(oldGroupId);
            App.ChangesMade = true;
            App.Output($"Ungrouped note 0x{only.Note:X2}.");
            return;
        }

        var newGroupId = AllocateNextGroupId();
        var affectedOldGroups = new HashSet<int>();

        foreach (var note in notes)
        {
            var sample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == note);
            if (sample is null)
            {
                continue;
            }

            if (sample.GroupId != -1)
            {
                affectedOldGroups.Add(sample.GroupId);
            }

            sample.GroupId = newGroupId;
        }

        foreach (var oldGroupId in affectedOldGroups)
        {
            DissolveUndersizedGroup(oldGroupId);
        }

        App.ChangesMade = true;
        App.Output($"Grouped {notes.Count} pads (group {newGroupId}).");
    }

    private static int AllocateNextGroupId()
    {
        var maxId = App.Project.LoadedSamples.Select(s => s.GroupId).DefaultIfEmpty(-1).Max();
        return maxId + 1;
    }

    private static void DissolveUndersizedGroup(int groupId)
    {
        if (groupId < 0)
        {
            return;
        }

        var members = App.Project.LoadedSamples.Where(s => s.GroupId == groupId).ToList();
        if (members.Count >= 2)
        {
            return;
        }

        foreach (var member in members)
        {
            member.GroupId = -1;
        }
    }

    private static void StopOtherGroupVoices(LoadedSample sample)
    {
        if (sample.GroupId < 0)
        {
            return;
        }

        foreach (var (note, handle) in App.ActivePlayVoices.ToList())
        {
            if (note == sample.Note)
            {
                continue;
            }

            var other = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == note);
            if (other?.GroupId != sample.GroupId)
            {
                continue;
            }

            App.AudioEngine.StopPlayback(handle);
            ClearPlayVoice(note);
        }
    }

    /// <summary>
    /// Play-mode pad press: polyphony across pads, one voice per pad.
    /// Looping pads toggle stop; one-shots re-trigger (Shift+tap stops a playing one-shot).
    /// Grouped pads are exclusive: starting one stops other active voices in the same group.
    /// </summary>
    public static void HandlePlayModePad(LoadedSample sample, PlayOneShotFinishedDelegate onVoicesChanged)
    {
        if (sample.InMemorySample is null)
        {
            return;
        }

        var note = sample.Note;
        if (TryGetPlayVoice(note, out var existingHandle))
        {
            App.AudioEngine.StopPlayback(existingHandle);
            ClearPlayVoice(note);

            if (sample.Loop || App.IsShiftHeld)
            {
                onVoicesChanged();
                return;
            }
        }

        StopOtherGroupVoices(sample);

        var handle = -1;
        handle = PlayLoadedSample(
            sample,
            () =>
            {
                ClearPlayVoiceIfHandle(note, handle);
                onVoicesChanged();
            },
            replaceCurrent: false);

        if (handle != -1)
        {
            RegisterPlayVoice(note, handle);
            onVoicesChanged();
        }
    }

    /// <summary>Starts playback. Returns the engine handle, or -1 on failure.</summary>
    public static int PlayLoadedSample(
        LoadedSample sample,
        PlayOneShotFinishedDelegate onFinished,
        int? fromSample = null,
        bool replaceCurrent = true)
    {
        if (sample.InMemorySample is null)
        {
            return -1;
        }

        try
        {
            if (replaceCurrent && App.CurrentlyPlayingSampleHandle != -1)
            {
                App.AudioEngine.StopPlayback(App.CurrentlyPlayingSampleHandle);
                App.CurrentlyPlayingSampleHandle = -1;
                App.CurrentlyPlayingNote = -1;
            }

            var regionStart = sample.StartSample;
            var handle = -1;
            handle = App.AudioEngine.PlayOneShot(
                sample.InMemorySample,
                regionStart,
                sample.EndSample,
                sample.Loop,
                sample.Volume,
                () =>
                {
                    if (replaceCurrent && App.CurrentlyPlayingSampleHandle == handle)
                    {
                        App.CurrentlyPlayingSampleHandle = -1;
                        App.CurrentlyPlayingNote = -1;
                    }

                    onFinished();
                },
                playbackStart: fromSample,
                playBackwards: sample.PlayBackwards);

            if (handle == -1)
            {
                if (replaceCurrent)
                {
                    App.CurrentlyPlayingNote = -1;
                }

                return -1;
            }

            if (replaceCurrent)
            {
                App.CurrentlyPlayingNote = sample.Note;
                App.CurrentlyPlayingSampleHandle = handle;
            }

            return handle;
        }
        catch (Exception ex)
        {
            if (replaceCurrent)
            {
                App.CurrentlyPlayingSampleHandle = -1;
                App.CurrentlyPlayingNote = -1;
            }

            App.Output($"Failed to play sample '{sample.FileName}': {ex.Message}");

            return -1;
        }
    }


    public static bool StartSamplingRecording()
    {
        try
        {
            StopInputLevelMonitoring();
            App.AudioEngine.RecordMono = App.RecordMono;
            App.AudioEngine.StartRecording();
            App.Output($"Recording for note 0x{App.CurrentlySelectedNote:X2} ({(App.RecordMono ? "mono" : "stereo")})...");
        }
        catch (Exception ex)
        {
            App.Output($"Failed to start recording: {ex.Message}");
            StartInputLevelMonitoring();
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
            StartInputLevelMonitoring();
            return;
        }
        catch (Exception ex)
        {
            App.Output($"Failed to save recording: {ex.Message}");
            StartInputLevelMonitoring();
            return;
        }

        AudioSystem.AssignSampleFromPath(note, destPath);
        var sample = App.Project.LoadedSamples.First(s => s.Note == note);
        sample.Loop = false;
        sample.PlayBackwards = false;
        sample.GroupId = -1;
        App.CurrentlySelectedNote = -1;
        App.ChangesMade = true;
        App.Output($"Recorded '{Path.GetFileName(destPath)}' to note 0x{note:X2}.");
        StartInputLevelMonitoring();
    }

    public static void CancelSamplingRecording()
    {
        App.AudioEngine.StopRecording();
        App.Output("Recording cancelled.");
        StartInputLevelMonitoring();
    }

    public static void StartInputLevelMonitoring()
    {
        if (App.AudioEngine.IsRecording || App.AudioEngine.IsInputMonitoring)
        {
            return;
        }

        try
        {
            App.AudioEngine.StartInputMonitoring(enableMonitor: false);
        }
        catch (Exception ex)
        {
            App.Output($"Failed to start input monitoring: {ex.Message}");
        }
    }

    public static void StopInputLevelMonitoring()
    {
        if (!App.AudioEngine.IsInputMonitoring || App.AudioEngine.IsRecording)
        {
            return;
        }

        App.AudioEngine.StopInputMonitoring();
    }

    private static string GetUniqueRecordingPath(string samplesFolder)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var path = Path.Combine(samplesFolder, $"recording_{timestamp}.wav");
        for (var i = 1; File.Exists(path); i++)
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
            App.Output("No ASIO drivers found. Audio disabled ? use 'audio' to list drivers when one is installed.");
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
            App.Output("Audio disabled ? connect your interface and run 'audio' to list available audio drivers.");
        }
    }

}

