namespace DiceMaster;

// Monotonic clock, shared by manual HTTP actions and automatic reconnects.
public sealed class NetworkBudget
{
    private readonly Func<long> now;
    private readonly Queue<long> requests = new(), connections = new();
    private readonly object sync = new();
    private long resumeAt;
    private int failures;
    private bool updateRequired;
    public bool UpdateRequired { get { lock(sync) return updateRequired; } }
    public bool Paused { get { lock(sync) return reason != null; } }
    private string? reason;
    public NetworkBudget(Func<long>? clock = null) { now = clock ?? (()=>Environment.TickCount64); }
    public string Message { get { lock(sync) return updateRequired ? "Please update DiceMaster to the latest version." : reason == null ? "" : reason + (now()<resumeAt ? $" Retry available in {(resumeAt-now()+999)/1000}s." : " Use Resume connection to try again."); } }
    private void Pause(string message, long delay) { reason=message; resumeAt=Math.Max(resumeAt,now()+delay); }
    private static void Trim(Queue<long> queue,long before) { while(queue.TryPeek(out var value) && value<=before)queue.Dequeue(); }
    public void Request()
    {
        lock(sync)
        {
            if(reason!=null)throw new NetworkPausedException(Message);
            Trim(requests,now()-60000);
            if(requests.Count>=60) { Pause("Requests paused to protect the relay.",60000); throw new NetworkPausedException(Message); }
            requests.Enqueue(now());
        }
    }
    public void Connect()
    {
        lock(sync)
        {
            if(reason!=null)throw new NetworkPausedException(Message);
            Trim(connections,now()-600000);
            if(connections.Count>=5) { Pause("Repeated reconnects paused to protect the relay.",300000); throw new NetworkPausedException(Message); }
            connections.Enqueue(now());
        }
    }
    public void Response(int status, TimeSpan? retryAfter = null)
    {
        lock(sync) if(status==426) { updateRequired=true; reason="Update required"; return; }
        lock(sync) if(status==429 || status>=500)
            Pause(status==429 ? "The relay is limiting requests. Connection paused." : "The relay is unavailable. Connection paused.",Math.Max(60000,(long)(retryAfter?.TotalMilliseconds ?? 0)));
    }
    public void TransportFailure()
    {
        lock(sync) if(++failures>=3 && reason==null)Pause("Connection paused after repeated network failures.",60000);
    }
    public bool Resume()
    {
        lock(sync)
        {
            if(updateRequired || reason==null || now()<resumeAt)return false;
            reason=null; failures=0; resumeAt=0;
            // Explicit recovery grants a fresh, bounded connection batch.
            connections.Clear();
            return true;
        }
    }
}
public sealed class NetworkPausedException(string message) : Exception(message);


