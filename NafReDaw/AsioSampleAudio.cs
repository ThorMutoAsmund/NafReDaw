// AsioSampleAudio.cs — portable ASIO-only sample playback and recording for NAudio.
//
// Drop this single file into a new project. It is NOT referenced by NafReZampler.
//
// Requirements:
//   - Target: net8.0-windows (or any Windows target with NAudio ASIO support)
//   - NuGet:  NAudio 2.2.1 or higher
//
// Quick start:
//
//   using NafAudio;
//
//   using var engine = new AsioSampleEngine();
//   engine.Start();                                          // first ASIO driver, 44.1 kHz stereo
//   var sample = AsioSampleEngine.LoadSample(@"C:\audio\kick.wav");
//   int handle = engine.PlayOneShot(sample, () => Console.WriteLine("done"));
//
//   engine.StartRecording();                                 // capture input while playback runs
//   // ... record ...
//   engine.SaveRecording(@"C:\audio\take.wav");
//
//   engine.Stop();
//
// Recording-only with input monitoring:
//
//   using var engine = new AsioSampleEngine();
//   engine.StartInputMonitoring(enableMonitor: true);
//   engine.BeginCapture();
//   // ... record ...
//   engine.EndCapture();
//   engine.SaveCapture(@"C:\audio\recording.wav");
//   engine.StopInputMonitoring();

using System.Collections.Generic;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NafAudio;


/// <summary>Identifies an ASIO driver (id and display name are the same driver name).</summary>
public sealed record AudioDeviceInfo(string Id, string Name);

public sealed class RecordingDataEventArgs : EventArgs
{
    public byte[] Buffer { get; }
    public int BytesRecorded { get; }

    public RecordingDataEventArgs(byte[] buffer, int bytesRecorded)
    {
        Buffer = buffer;
        BytesRecorded = bytesRecorded;
    }
}

public interface IAudioPlayback : IDisposable
{
    void Init(IWaveProvider source);
    void Play();
    void Stop();
    event EventHandler? PlaybackStopped;
}

public interface IAudioRecording : IDisposable
{
    event EventHandler<RecordingDataEventArgs>? DataAvailable;
    void StartRecording();
    void StopRecording();
}

public interface IAudioBackend
{
    string Name { get; }
    IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices();
    IReadOnlyList<AudioDeviceInfo> GetRecordingDevices();
    IAudioPlayback CreatePlayback(string deviceId);
    IAudioRecording CreateRecording(string deviceId, WaveFormat format, IWaveProvider? monitorPlayback = null);
}


/// <summary>
/// ASIO audio backend. Uses a single shared <see cref="AsioOut"/> for playback and recording
/// on the same driver, because most ASIO drivers allow only one client.
/// </summary>
public sealed class AsioAudioBackend : IAudioBackend
{
    public string Name => "ASIO";

    private AsioOut? _sharedAsioOut;
    private string? _sharedDriverName;
    private readonly object _sharedLock = new();
    private bool _sharedInstanceCreatedEventRaised;
    private event EventHandler<RecordingDataEventArgs>? SharedRecordingDataAvailable;

    public event EventHandler? SharedInstanceCreated;

    public bool HasSharedInstance
    {
        get { lock (_sharedLock) { return _sharedAsioOut != null; } }
    }

    public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => GetDriverList();

    public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => GetDriverList();

    public IAudioPlayback CreatePlayback(string deviceId)
    {
        return new AsioPlayback(this, ResolveDeviceId(deviceId));
    }

    public IAudioRecording CreateRecording(string deviceId, WaveFormat format, IWaveProvider? monitorPlayback = null)
    {
        return new AsioRecording(this, ResolveDeviceId(deviceId), format, monitorPlayback);
    }

    internal void NotifySharedInstanceNowPlaying()
    {
        lock (_sharedLock)
        {
            if (_sharedInstanceCreatedEventRaised || _sharedAsioOut == null)
            {
                return;
            }

            _sharedInstanceCreatedEventRaised = true;
        }

        SharedInstanceCreated?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<AudioDeviceInfo> GetDriverList()
    {
        try
        {
            var names = AsioOut.GetDriverNames();
            var list = new List<AudioDeviceInfo>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                list.Add(new AudioDeviceInfo(names[i], names[i]));
            }

            return list;
        }
        catch
        {
            return Array.Empty<AudioDeviceInfo>();
        }
    }

    private static string ResolveDeviceId(string deviceId)
    {
        if (!string.IsNullOrEmpty(deviceId) && deviceId != "-1")
        {
            return deviceId;
        }

        var names = AsioOut.GetDriverNames();
        return names.Length > 0 ? names[0] : throw new InvalidOperationException("No ASIO driver available.");
    }

    private void OnSharedAsioAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
    {
        var bytes = ConvertAsioInputToPcm16(e);
        if (bytes.Length == 0)
        {
            return;
        }

        SharedRecordingDataAvailable?.Invoke(this, new RecordingDataEventArgs(bytes, bytes.Length));
    }

    private static byte[] ConvertAsioInputToPcm16(AsioAudioAvailableEventArgs e)
    {
        int channelCount = e.InputBuffers?.Length ?? 0;
        int sampleCount = e.SamplesPerBuffer * channelCount;
        if (sampleCount <= 0)
        {
            return Array.Empty<byte>();
        }

        var floats = new float[sampleCount];
        e.GetAsInterleavedSamples(floats);
        var bytes = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)Math.Clamp(floats[i] * 32767, -32768, 32767);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)(s >> 8);
        }

        return bytes;
    }

    private sealed class AsioPlayback : IAudioPlayback
    {
        private readonly AsioAudioBackend _backend;
        private readonly string _driverName;
        private AsioOut? _asioOut;

        public event EventHandler? PlaybackStopped;

        public AsioPlayback(AsioAudioBackend backend, string driverName)
        {
            _backend = backend;
            _driverName = driverName;
        }

        public void Init(IWaveProvider source)
        {
            _asioOut = new AsioOut(_driverName);
            _asioOut.PlaybackStopped += (_, _) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
            _asioOut.InitRecordAndPlayback(source, AsioSampleEngine.Channels, AsioSampleEngine.SampleRate);
            lock (_backend._sharedLock)
            {
                _backend._sharedAsioOut = _asioOut;
                _backend._sharedDriverName = _driverName;
                _asioOut.AudioAvailable += _backend.OnSharedAsioAudioAvailable;
            }
        }

        public void Play()
        {
            _asioOut?.Play();
            _backend.NotifySharedInstanceNowPlaying();
        }

        public void Stop() => _asioOut?.Stop();

        public void Dispose()
        {
            lock (_backend._sharedLock)
            {
                if (_backend._sharedAsioOut == _asioOut)
                {
                    if (_asioOut != null)
                    {
                        _asioOut.AudioAvailable -= _backend.OnSharedAsioAudioAvailable;
                    }

                    _backend._sharedAsioOut = null;
                    _backend._sharedDriverName = null;
                    _backend._sharedInstanceCreatedEventRaised = false;
                }
            }

            _asioOut?.Dispose();
            _asioOut = null;
        }
    }

    private sealed class SilenceWaveProvider : IWaveProvider
    {
        public SilenceWaveProvider(WaveFormat waveFormat) => WaveFormat = waveFormat;
        public WaveFormat WaveFormat { get; }
        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }

    private sealed class AsioRecording : IAudioRecording
    {
        private readonly AsioAudioBackend _backend;
        private readonly string _driverName;
        private readonly WaveFormat _format;
        private readonly IWaveProvider? _monitorPlayback;
        private AsioOut? _standaloneAsioOut;

        public event EventHandler<RecordingDataEventArgs>? DataAvailable;

        public AsioRecording(AsioAudioBackend backend, string driverName, WaveFormat format, IWaveProvider? monitorPlayback)
        {
            _backend = backend;
            _driverName = driverName;
            _format = format;
            _monitorPlayback = monitorPlayback;
        }

        public void StartRecording()
        {
            lock (_backend._sharedLock)
            {
                if (_backend._sharedAsioOut != null && _backend._sharedDriverName == _driverName)
                {
                    _backend.SharedRecordingDataAvailable += OnSharedRecordingData;
                    return;
                }
            }

            var playbackSource = _monitorPlayback ?? new SilenceWaveProvider(_format);
            _standaloneAsioOut = new AsioOut(_driverName);
            _standaloneAsioOut.InitRecordAndPlayback(playbackSource, AsioSampleEngine.Channels, AsioSampleEngine.SampleRate);
            _standaloneAsioOut.AudioAvailable += OnStandaloneAudioAvailable;
            _standaloneAsioOut.Play();
        }

        private void OnStandaloneAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
        {
            var bytes = ConvertAsioInputToPcm16(e);
            if (bytes.Length == 0)
            {
                return;
            }

            DataAvailable?.Invoke(this, new RecordingDataEventArgs(bytes, bytes.Length));
        }

        private void OnSharedRecordingData(object? sender, RecordingDataEventArgs e)
        {
            DataAvailable?.Invoke(this, e);
        }

        public void StopRecording()
        {
            lock (_backend._sharedLock)
            {
                _backend.SharedRecordingDataAvailable -= OnSharedRecordingData;
            }

            if (_standaloneAsioOut != null)
            {
                _standaloneAsioOut.AudioAvailable -= OnStandaloneAudioAvailable;
                _standaloneAsioOut.Stop();
                _standaloneAsioOut.Dispose();
                _standaloneAsioOut = null;
            }
        }

        public void Dispose() => StopRecording();
    }
}



/// <summary>In-memory sample as IEEE float interleaved channels.</summary>
public sealed class InMemorySample
{
    public string FilePath { get; }
    public string Name { get; }
    public float[] Samples { get; }
    public WaveFormat WaveFormat { get; }
    public int SampleCount => Samples.Length;

    public InMemorySample(string filePath, string name, float[] samples, WaveFormat waveFormat)
    {
        FilePath = filePath;
        Name = name;
        Samples = samples;
        WaveFormat = waveFormat;
    }
}

/// <summary>Plays samples from a float array. Supports one-shot or looping with optional delay.</summary>
public sealed class FloatArraySampleProvider : ISampleProvider
{
    private readonly float[] _samples;
    private readonly bool _loop;
    private long _position;
    private bool _finishedRaised;
    private int? _stopAfter;
    private int _startAfter;

    public FloatArraySampleProvider(float[] samples, WaveFormat waveFormat, bool loop = false, int startAfter = 0)
    {
        _samples = samples;
        WaveFormat = waveFormat;
        _loop = loop;
        _startAfter = startAfter;
    }

    public event Action? Finished;
    public event Action? RestartingLoop;
    public WaveFormat WaveFormat { get; }

    public void StopAfter(int stopAfter) => _stopAfter = stopAfter;

    public int Read(float[] buffer, int offset, int count)
    {
        int totalSamples = _samples.Length;
        int read = 0;

        if (_startAfter > 0)
        {
            read = (int)Math.Min(count, _startAfter);
            for (int i = 0; i < read; i++)
            {
                buffer[offset + i] = 0;
            }

            _startAfter -= read;
        }

        while (read < count)
        {
            if (_position >= totalSamples)
            {
                if (!_loop)
                {
                    if (!_finishedRaised)
                    {
                        _finishedRaised = true;
                        Finished?.Invoke();
                    }

                    break;
                }

                RestartingLoop?.Invoke();
                _position = 0;
            }

            int toRead = (int)Math.Min(count - read, totalSamples - _position);
            if (_stopAfter.HasValue)
            {
                toRead = Math.Min(toRead, _stopAfter.Value);
                _stopAfter -= toRead;
            }

            for (int i = 0; i < toRead; i++)
            {
                buffer[offset + read + i] = _samples[_position + i];
            }

            _position += toRead;
            read += toRead;

            if (_stopAfter.HasValue && _stopAfter.Value == 0)
            {
                break;
            }
        }

        return read;
    }
}

/// <summary>Mixes multiple IEEE float sample providers into one output stream.</summary>
public sealed class SmartMixingSampleProvider : ISampleProvider
{
    private readonly List<ISampleProvider> _sources = new();
    private readonly Dictionary<ISampleProvider, FloatArraySampleProvider> _rawLookup = new();
    private float[] _sourceBuffer = Array.Empty<float>();

    public IEnumerable<ISampleProvider> MixerInputs => _sources;
    public bool ReadFully { get; set; }
    public WaveFormat WaveFormat { get; }

    public SmartMixingSampleProvider(WaveFormat waveFormat)
    {
        if (waveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
        {
            throw new ArgumentException("Mixer wave format must be IEEE float");
        }

        WaveFormat = waveFormat;
    }

    public void AddMixerInput(ISampleProvider mixerInput, FloatArraySampleProvider? raw)
    {
        lock (_sources)
        {
            if (_sources.Count >= 1024)
            {
                throw new InvalidOperationException("Too many mixer inputs");
            }

            _sources.Add(mixerInput);
            if (raw is not null)
            {
                _rawLookup[mixerInput] = raw;
            }
        }
    }

    public void RemoveMixerInput(ISampleProvider mixerInput)
    {
        lock (_sources)
        {
            _sources.Remove(mixerInput);
            _rawLookup.Remove(mixerInput);
        }
    }

    public void RemoveMixerInputAfter(ISampleProvider mixerInput, int removeAfter)
    {
        if (removeAfter == 0)
        {
            RemoveMixerInput(mixerInput);
            return;
        }

        if (_rawLookup.TryGetValue(mixerInput, out var raw))
        {
            raw.StopAfter(removeAfter);
        }
    }

    public void RemoveAllMixerInputs()
    {
        lock (_sources)
        {
            _sources.Clear();
            _rawLookup.Clear();
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int num = 0;
        _sourceBuffer = BufferHelpers.Ensure(_sourceBuffer, count);
        lock (_sources)
        {
            for (int n = _sources.Count - 1; n >= 0; n--)
            {
                ISampleProvider source = _sources[n];
                int samplesRead = source.Read(_sourceBuffer, 0, count);
                int pos = offset;
                for (int i = 0; i < samplesRead; i++)
                {
                    if (i >= num)
                    {
                        buffer[pos++] = _sourceBuffer[i];
                    }
                    else
                    {
                        buffer[pos++] += _sourceBuffer[i];
                    }
                }

                num = Math.Max(samplesRead, num);
                if (samplesRead < count)
                {
                    _sources.RemoveAt(n);
                    _rawLookup.Remove(source);
                }
            }
        }

        if (ReadFully && num < count)
        {
            for (int i = offset + num; i < offset + count; i++)
            {
                buffer[i] = 0f;
            }

            num = count;
        }

        return num;
    }
}

/// <summary>Adapts <see cref="SmartMixingSampleProvider"/> to <see cref="WaveStream"/> for ASIO playback.</summary>
public sealed class SmartMixerWaveStream : WaveStream
{
    private readonly SmartMixingSampleProvider _mixer;
    private readonly float[] _floatBuffer;

    public SmartMixerWaveStream(SmartMixingSampleProvider mixer)
    {
        _mixer = mixer;
        _floatBuffer = new float[4096];
    }

    public override WaveFormat WaveFormat => _mixer.WaveFormat;
    public override long Length => long.MaxValue;
    public override long Position { get; set; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int sampleCount = count / 4;
        if (sampleCount > _floatBuffer.Length)
        {
            sampleCount = _floatBuffer.Length;
        }

        int samplesRead = _mixer.Read(_floatBuffer, 0, sampleCount);
        int bytesToCopy = samplesRead * 4;
        Buffer.BlockCopy(_floatBuffer, 0, buffer, offset, bytesToCopy);
        return bytesToCopy;
    }
}


/// <summary>
/// All-in-one ASIO sample playback and recording engine.
/// </summary>
public sealed class AsioSampleEngine : IDisposable
{
    public const int SampleRate = 44100;
    public const int Channels = 2;

    public static WaveFormat MixerWaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
    public static WaveFormat RecordingWaveFormat { get; } = new(SampleRate, 16, Channels);

    private readonly AsioAudioBackend _backend = new();
    private readonly object _playbackLock = new();
    private readonly Dictionary<int, PlaybackEntry> _playbacks = new();
    private readonly object _captureLock = new();

    private IAudioPlayback? _mixerPlayback;
    private SmartMixingSampleProvider? _mixer;
    private SmartMixerWaveStream? _mixerStream;
    private IAudioRecording? _recording;
    private MemoryStream? _recordingBuffer;
    private EventHandler<RecordingDataEventArgs>? _recordingHandler;

    private IAudioRecording? _inputMonitor;
    private BufferedWaveProvider? _monitorBuffer;
    private MemoryStream _captureBuffer = new();
    private bool _captureActive;
    private volatile float _peakLeft;
    private volatile float _peakRight;
    private int _nextPlaybackHandle = 1;

    /// <summary>ASIO driver name for playback. Use "-1" for the first available driver.</summary>
    public string PlaybackDeviceId { get; set; } = "-1";

    /// <summary>ASIO driver name for recording. Use "-1" for the first available driver.</summary>
    public string RecordingDeviceId { get; set; } = "-1";

    public bool IsPlaybackRunning => _mixerPlayback != null;
    public bool IsRecording => _recordingBuffer != null;
    public bool IsInputMonitoring => _inputMonitor != null;

    public static IReadOnlyList<AudioDeviceInfo> GetDrivers() => new AsioAudioBackend().GetPlaybackDevices();

    /// <summary>Loads a WAV or MP3 file into memory as IEEE float samples.</summary>
    public static InMemorySample LoadSample(string filePath)
    {
        using var reader = new AudioFileReader(filePath);
        var samples = new List<float>();
        var buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
        int samplesRead;
        while ((samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < samplesRead; i++)
            {
                samples.Add(buffer[i]);
            }
        }

        var name = Path.GetFileNameWithoutExtension(filePath);
        return new InMemorySample(filePath, name, samples.ToArray(), reader.WaveFormat);
    }

    /// <summary>Starts the mixer and ASIO playback output. Required before PlayOneShot/PlayLoop.</summary>
    public void StartPlayback()
    {
        if (_mixerPlayback != null)
        {
            return;
        }

        _mixer = new SmartMixingSampleProvider(MixerWaveFormat) { ReadFully = true };
        _mixerStream = new SmartMixerWaveStream(_mixer);
        _mixerPlayback = _backend.CreatePlayback(PlaybackDeviceId);
        _mixerPlayback.Init(_mixerStream);
        _mixerPlayback.Play();
    }

    /// <summary>Stops playback, recording, and input monitoring.</summary>
    public void Stop()
    {
        StopRecording();
        StopAllPlayback();
        StopInputMonitoring();

        _mixerPlayback?.Stop();
        _mixerPlayback?.Dispose();
        _mixerPlayback = null;
        _mixerStream = null;
        _mixer = null;
    }

    /// <summary>Plays a sample once. Returns a handle that can be passed to <see cref="StopPlayback"/>.</summary>
    public int PlayOneShot(InMemorySample sample, Action? onFinished = null)
    {
        EnsurePlaybackStarted();
        int handle = _nextPlaybackHandle++;
        var raw = new FloatArraySampleProvider(sample.Samples, sample.WaveFormat, loop: false);
        raw.Finished += () =>
        {
            RemovePlayback(handle);
            onFinished?.Invoke();
        };

        var input = ToMixerFormat(raw, sample.WaveFormat);
        lock (_playbackLock)
        {
            _playbacks[handle] = new PlaybackEntry(input, raw);
        }

        _mixer!.AddMixerInput(input, raw);
        return handle;
    }

    /// <summary>Plays a sample in a loop. Optional <paramref name="startAfterSamples"/> inserts silence first.</summary>
    public int PlayLoop(InMemorySample sample, int startAfterSamples = 0, Action? onLoopRestart = null, Action? onFinished = null)
    {
        EnsurePlaybackStarted();
        int handle = _nextPlaybackHandle++;
        var raw = new FloatArraySampleProvider(sample.Samples, sample.WaveFormat, loop: true, startAfter: startAfterSamples);
        if (onLoopRestart != null)
        {
            raw.RestartingLoop += onLoopRestart;
        }

        raw.Finished += () =>
        {
            RemovePlayback(handle);
            onFinished?.Invoke();
        };

        var input = ToMixerFormat(raw, sample.WaveFormat);
        lock (_playbackLock)
        {
            _playbacks[handle] = new PlaybackEntry(input, raw);
        }

        _mixer!.AddMixerInput(input, raw);
        return handle;
    }

    public void StopPlayback(int handle)
    {
        if (_mixer == null)
        {
            return;
        }

        lock (_playbackLock)
        {
            if (_playbacks.TryGetValue(handle, out var entry))
            {
                _mixer.RemoveMixerInput(entry.MixerInput);
                _playbacks.Remove(handle);
            }
        }
    }

    public void StopAllPlayback()
    {
        if (_mixer == null)
        {
            return;
        }

        lock (_playbackLock)
        {
            foreach (var entry in _playbacks.Values)
            {
                _mixer.RemoveMixerInput(entry.MixerInput);
            }

            _playbacks.Clear();
        }
    }

    /// <summary>
    /// Starts recording from the ASIO input. When playback is active on the same driver,
    /// recording attaches to the shared ASIO instance without stopping playback.
    /// </summary>
    public void StartRecording(int skipInitialBytes = 0)
    {
        if (_recordingBuffer != null)
        {
            return;
        }

        _recordingBuffer = new MemoryStream();
        var skipRemaining = skipInitialBytes;

        _recordingHandler = (_, e) =>
        {
            var buffer = e.Buffer;
            var length = e.BytesRecorded;
            if (skipRemaining > 0)
            {
                if (skipRemaining >= length)
                {
                    skipRemaining -= length;
                    return;
                }

                var offset = skipRemaining;
                _recordingBuffer!.Write(buffer, offset, length - offset);
                skipRemaining = 0;
            }
            else
            {
                _recordingBuffer!.Write(buffer, 0, length);
            }
        };

        _recording = _backend.CreateRecording(RecordingDeviceId, RecordingWaveFormat);
        _recording.DataAvailable += _recordingHandler;
        _recording.StartRecording();
    }

    /// <summary>Stops recording and returns the captured PCM16 stereo bytes.</summary>
    public byte[] StopRecording()
    {
        if (_recording == null || _recordingBuffer == null)
        {
            return Array.Empty<byte>();
        }

        if (_recordingHandler != null)
        {
            _recording.DataAvailable -= _recordingHandler;
        }

        _recording.StopRecording();
        _recording.Dispose();
        _recording = null;
        _recordingHandler = null;

        var data = _recordingBuffer.ToArray();
        _recordingBuffer.Dispose();
        _recordingBuffer = null;
        return data;
    }

    /// <summary>Saves the current recording buffer to a WAV file and clears the recording session.</summary>
    public void SaveRecording(string filePath)
    {
        byte[] data;
        if (_recordingBuffer != null)
        {
            data = StopRecording();
        }
        else
        {
            data = Array.Empty<byte>();
        }

        if (data.Length == 0)
        {
            throw new InvalidOperationException("No recording data to save.");
        }

        using var writer = new WaveFileWriter(filePath, RecordingWaveFormat);
        writer.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Starts always-on input monitoring (for level meters). Optionally routes input to ASIO output for monitoring.
    /// Does not require <see cref="StartPlayback"/>.
    /// </summary>
    public void StartInputMonitoring(bool enableMonitor = false)
    {
        StopInputMonitoring();

        if (enableMonitor)
        {
            _monitorBuffer = new BufferedWaveProvider(RecordingWaveFormat)
            {
                BufferLength = RecordingWaveFormat.AverageBytesPerSecond * 4,
                DiscardOnBufferOverflow = true
            };
        }

        _inputMonitor = _backend.CreateRecording(RecordingDeviceId, RecordingWaveFormat, _monitorBuffer);
        _inputMonitor.DataAvailable += OnInputDataAvailable;
        _inputMonitor.StartRecording();
    }

    public void StopInputMonitoring()
    {
        if (_inputMonitor != null)
        {
            _inputMonitor.DataAvailable -= OnInputDataAvailable;
            _inputMonitor.StopRecording();
            _inputMonitor.Dispose();
            _inputMonitor = null;
        }

        _monitorBuffer = null;
        _captureActive = false;
        lock (_captureLock)
        {
            _captureBuffer.Dispose();
            _captureBuffer = new MemoryStream();
        }
    }

    /// <summary>When input monitoring is active, begins writing captured audio to an in-memory buffer.</summary>
    public void BeginCapture()
    {
        _captureActive = true;
        lock (_captureLock)
        {
            _captureBuffer.Dispose();
            _captureBuffer = new MemoryStream();
        }
    }

    /// <summary>Stops writing captured audio to the in-memory buffer.</summary>
    public void EndCapture() => _captureActive = false;

    public long GetCapturedByteCount()
    {
        lock (_captureLock)
        {
            return _captureBuffer.Length;
        }
    }

    /// <summary>Saves captured audio (from <see cref="BeginCapture"/>) to a WAV file.</summary>
    public void SaveCapture(string filePath)
    {
        byte[] data;
        lock (_captureLock)
        {
            if (_captureBuffer.Length == 0)
            {
                throw new InvalidOperationException("No captured audio to save.");
            }

            data = _captureBuffer.ToArray();
        }

        using var writer = new WaveFileWriter(filePath, RecordingWaveFormat);
        writer.Write(data, 0, data.Length);
    }

    /// <summary>Returns peak input levels (0–1) for left and right channels, with optional decay.</summary>
    public (double Left, double Right) GetInputLevels(float decayFactor = 0.92f)
    {
        var left = Math.Min(1, _peakLeft);
        var right = Math.Min(1, _peakRight);
        _peakLeft *= decayFactor;
        _peakRight *= decayFactor;
        return (left, right);
    }

    public static string FormatDuration(long pcmByteCount)
    {
        const int bytesPerSecond = SampleRate * Channels * 2;
        var totalSeconds = pcmByteCount / (double)bytesPerSecond;
        var minutes = (int)(totalSeconds / 60);
        var secondsPart = totalSeconds - minutes * 60;
        var seconds = (int)secondsPart;
        var hundredths = (int)((secondsPart - seconds) * 100);
        return $"{minutes:D2}:{seconds:D2}.{hundredths:D2}";
    }

    public void Dispose() => Stop();

    private void EnsurePlaybackStarted()
    {
        if (_mixerPlayback == null)
        {
            StartPlayback();
        }
    }

    private void RemovePlayback(int handle)
    {
        lock (_playbackLock)
        {
            _playbacks.Remove(handle);
        }
    }

    private static ISampleProvider ToMixerFormat(ISampleProvider provider, WaveFormat sourceFormat)
    {
        if (sourceFormat.Channels == 1)
        {
            provider = new MonoToStereoSampleProvider(provider);
        }

        if (sourceFormat.SampleRate != SampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, SampleRate);
        }

        return provider;
    }

    private void OnInputDataAvailable(object? sender, RecordingDataEventArgs e)
    {
        const int bytesPerSample = 2;
        var sampleCount = e.BytesRecorded / bytesPerSample / Channels;
        var maxLeft = 0f;
        var maxRight = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            var position = i * bytesPerSample * Channels;
            var left = Math.Abs(BitConverter.ToInt16(e.Buffer, position) / 32768f);
            var right = Math.Abs(BitConverter.ToInt16(e.Buffer, position + 2) / 32768f);
            if (left > maxLeft) maxLeft = left;
            if (right > maxRight) maxRight = right;
        }

        _peakLeft = maxLeft;
        _peakRight = maxRight;

        if (_monitorBuffer != null)
        {
            _monitorBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        if (_captureActive)
        {
            lock (_captureLock)
            {
                _captureBuffer.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }
    }

    private sealed record PlaybackEntry(ISampleProvider MixerInput, FloatArraySampleProvider Raw);
}


