namespace PixelRealms.Server.Hosting;

/// <summary>
/// HU-072: contadores de red (mensajes y bytes dentro/fuera) que las sesiones incrementan con Interlocked; el GameLoop toma
/// una muestra por segundo (`Sample`) y `/admin/stats` publica las tasas de la última muestra.
/// </summary>
public sealed class NetMetrics
{
    private long _messagesIn, _messagesOut, _bytesIn, _bytesOut;
    private long _lastIn, _lastOut, _lastBytesIn, _lastBytesOut, _lastAlloc, _lastSampleMs;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public double UptimeSec => (DateTimeOffset.UtcNow - StartedAt).TotalSeconds;

    public long MessagesIn => Interlocked.Read(ref _messagesIn);

    public long MessagesOut => Interlocked.Read(ref _messagesOut);

    public long BytesIn => Interlocked.Read(ref _bytesIn);

    public long BytesOut => Interlocked.Read(ref _bytesOut);

    public double MessagesInPerSec { get; private set; }

    public double MessagesOutPerSec { get; private set; }

    public double BytesInPerSec { get; private set; }

    public double BytesOutPerSec { get; private set; }

    /// <summary>Bytes asignados por segundo en todo el proceso (GC.GetTotalAllocatedBytes); no se puede desglosar por instancia.</summary>
    public double AllocBytesPerSec { get; private set; }

    public void RecordIn(int bytes) { Interlocked.Increment(ref _messagesIn); Interlocked.Add(ref _bytesIn, bytes); }

    public void RecordOut(int bytes) { Interlocked.Increment(ref _messagesOut); Interlocked.Add(ref _bytesOut, bytes); }

    /// <summary>Lo llama el GameLoop ~1 vez por segundo (hilo del tick).</summary>
    public void Sample(long nowMs)
    {
        var alloc = GC.GetTotalAllocatedBytes();
        if (_lastSampleMs != 0 && nowMs > _lastSampleMs)
        {
            var sec = (nowMs - _lastSampleMs) / 1000.0;
            var mi = MessagesIn; var mo = MessagesOut; var bi = BytesIn; var bo = BytesOut;
            MessagesInPerSec = (mi - _lastIn) / sec;
            MessagesOutPerSec = (mo - _lastOut) / sec;
            BytesInPerSec = (bi - _lastBytesIn) / sec;
            BytesOutPerSec = (bo - _lastBytesOut) / sec;
            AllocBytesPerSec = (alloc - _lastAlloc) / sec;
            _lastIn = mi; _lastOut = mo; _lastBytesIn = bi; _lastBytesOut = bo;
        }
        else
        {
            _lastIn = MessagesIn; _lastOut = MessagesOut; _lastBytesIn = BytesIn; _lastBytesOut = BytesOut;
        }
        _lastAlloc = alloc;
        _lastSampleMs = nowMs;
    }
}
