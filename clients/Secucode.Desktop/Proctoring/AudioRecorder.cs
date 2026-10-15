using System.IO;
using NAudio.Utils;
using NAudio.Wave;

namespace Secucode.Desktop.Proctoring;

// Microphone recording in 60 s WAV chunks (16 kHz mono, about 1.9 MB each: well under the 10 MB evidence limit).
public sealed class AudioRecorder : IDisposable
{
    public static readonly TimeSpan ChunkLength = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private WaveInEvent? _input;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private Timer? _rotate;
    private bool _stopped;

    // Raised on a background thread with a complete WAV file.
    public event Action<byte[]>? ChunkReady;

    // False when there is no usable microphone; the caller records AudioUnavailable instead of blocking the exam.
    public bool Start(out string? error)
    {
        try
        {
            _input = new WaveInEvent { WaveFormat = new WaveFormat(16000, 1) };
            _input.DataAvailable += (_, a) => { lock (_lock) _writer?.Write(a.Buffer, 0, a.BytesRecorded); };
            lock (_lock) NewChunkLocked();
            _input.StartRecording();
            _rotate = new Timer(_ => Rotate(), null, ChunkLength, ChunkLength);
            error = null;
            return true;
        }
        catch (Exception e)
        {
            _input?.Dispose();
            _input = null;
            error = e.Message;
            return false;
        }
    }

    // Stops recording and emits the last, partial chunk.
    public void Stop()
    {
        _rotate?.Dispose();
        _rotate = null;
        _input?.StopRecording();
        byte[]? last;
        lock (_lock)
        {
            _stopped = true;
            last = FinishChunkLocked();
        }
        if (last is not null) Raise(last);
    }

    private void Rotate()
    {
        byte[]? done;
        lock (_lock)
        {
            if (_stopped) return;
            done = FinishChunkLocked();
            NewChunkLocked();
        }
        if (done is not null) Raise(done);
    }

    // A throwing handler must not crash the timer thread.
    private void Raise(byte[] chunk)
    {
        try { ChunkReady?.Invoke(chunk); }
        catch (Exception) { }
    }

    private void NewChunkLocked()
    {
        _buffer = new MemoryStream();
        _writer = new WaveFileWriter(new IgnoreDisposeStream(_buffer), _input!.WaveFormat);
    }

    private byte[]? FinishChunkLocked()
    {
        if (_writer is null || _buffer is null) return null;
        _writer.Dispose(); // writes the final sizes into the WAV header
        var bytes = _buffer.ToArray();
        _writer = null;
        _buffer = null;
        return bytes.Length > 44 ? bytes : null; // header only: nothing was recorded
    }

    public void Dispose()
    {
        Stop();
        _input?.Dispose();
        _input = null;
    }
}
