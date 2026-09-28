namespace Jellyfin.Plugin.AutoLut;

// Temporary opt-outs only. An enable request can never grant administrator permission.
public sealed class WebPreferences
{
    private readonly HashSet<(Guid User, string Device, Guid Item)> _disabled = [];
    private readonly object _gate = new();
    public SemaphoreSlim Changes { get; } = new(1, 1);
    public bool Enabled(Guid user, string device, Guid item)
    {
        lock (_gate) return !_disabled.Contains((user, device, item));
    }
    public bool Set(Guid user, string device, Guid item, bool enabled)
    {
        lock (_gate)
        {
            var key = (user, device, item);
            if (enabled) { _disabled.Remove(key); return true; }
            if (_disabled.Contains(key)) return true;
            if (_disabled.Count >= 512) return false;
            return _disabled.Add(key);
        }
    }
}
