using NAudio.Wave;

namespace Monolith.Voice.Services;

public class NaudioVadCapture : IDisposable
{
    private readonly int _sampleRate;
    private readonly int _bufferMs;
    private WaveInEvent? _waveIn;

    public event Action<byte[], int>? OnAudioData;
    public event Action? OnRecordingStopped;

    public NaudioVadCapture(int sampleRate = 16000, int frameMs = 20)
    {
        _sampleRate = sampleRate;
        _bufferMs = frameMs * 3;
    }

    public void Start()
    {
        Stop();

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(_sampleRate, 16, 1),
            BufferMilliseconds = _bufferMs
        };

        _waveIn.DataAvailable += (s, e) =>
            OnAudioData?.Invoke(e.Buffer, e.BytesRecorded);

        _waveIn.RecordingStopped += (s, e) =>
            OnRecordingStopped?.Invoke();

        _waveIn.StartRecording();
    }

    public void Stop()
    {
        if (_waveIn != null)
        {
            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
