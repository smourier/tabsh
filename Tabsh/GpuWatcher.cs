namespace Tabsh;

// the sampling runs on a thread of its own, and nothing may touch the sampler until that thread has stopped.
// Asking it to stop is not enough, one last sample is still owed and it reads a buffer that disposing would free.
internal sealed class GpuWatcher : IDisposable
{
    private const int _sampleMilliseconds = 200;
    private const int _joinMilliseconds = 5000;
    private const string _threadName = "tabsh gpu sampler";

    private readonly ManualResetEventSlim _stop = new(false);
    private readonly Thread? _thread;

    public GpuWatcher(GpuSampler? sampler, Func<List<uint>> running)
    {
        ArgumentNullException.ThrowIfNull(running);

        if (sampler == null)
            return;

        _thread = new Thread(() =>
        {
            while (!_stop.Wait(_sampleMilliseconds))
            {
                sampler.Sample(running());
            }

            // a GPU counter disappears with the process that owned it, so a last reading is taken on the way out.
            sampler.Sample(running());
        })
        { IsBackground = true, Name = _threadName };

        _thread.Start();
    }

    // the thread is waited for, not just signalled, since the caller goes on to read what it was writing.
    // A wait that runs out leaves the event behind rather than disposing one the thread is still sitting on.
    public void Dispose()
    {
        _stop.Set();
        if (_thread == null || _thread.Join(_joinMilliseconds))
        {
            _stop.Dispose();
        }
    }
}
