using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace DiceMaster;

// Network work never touches ImGui. Immutable results enter the UI through a queue.
public sealed class RelayClient : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim gate = new(1);
    private readonly ConcurrentQueue<Action> updates = new();
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private string participantToken = "", gmToken = "";
    private long sequence, gmExpires;
    private long clockServer, clockStamp = Stopwatch.GetTimestamp();
    private volatile bool joined;
    private readonly Task pollTask;
    private readonly CredentialStore credentialStore;
    public bool HasSavedCredential => credentialStore.Exists;
    public string AuthenticationStatus { get; private set; } = "Not authenticated";
    public string Room { get; private set; } = "";
    public string ParticipantId { get; private set; } = "";
    public string Status { get; private set; } = "Not connected";
    public bool Busy { get; private set; }
    public bool Joined => joined;
    public bool CanManage { get; private set; }
    public Participant[] Participants { get; private set; } = [];
    public PublicRoom[] PublicRooms { get; private set; } = [];
    public string BrowserStatus { get; private set; } = "Refresh to find public tables.";
    public Dictionary<string, int> Modifiers { get; private set; } = [];
    public List<Roll> Rolls { get; } = [];
    public long ServerNow => Interlocked.Read(ref clockServer) + (long)Stopwatch.GetElapsedTime(Interlocked.Read(ref clockStamp)).TotalMilliseconds;

    public RelayClient(CredentialStore credentialStore) { this.credentialStore=credentialStore; pollTask = Task.Run(PollLoop); }
    public void Drain()
    {
        while (updates.TryDequeue(out var action)) action();
        if (CanManage && ServerNow >= gmExpires) ClearPrivilege();
    }
    private void ClearPrivilege() { CanManage = false; Modifiers.Clear(); }
    public void Run(Func<Task> action)
    {
        if (Busy) return;
        Busy = true;
        _ = Execute(action);
    }
    private async Task Execute(Func<Task> action)
    {
        try
        {
            await gate.WaitAsync(stop.Token);
            try { await action(); } finally { gate.Release(); }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (OperationCanceledException) { updates.Enqueue(() => Status = "The relay took too long to respond. Please retry."); }
        catch (ArgumentException e) { updates.Enqueue(() => Status = e.Message); }
        catch (HttpRequestException e)
        {
            updates.Enqueue(() => { Status = e.StatusCode == HttpStatusCode.Forbidden ? "Authorization required or expired." : "Request failed. Check the relay and room connection."; ClearPrivilege(); });
        }
        catch { updates.Enqueue(() => { Status = "Could not complete the request."; ClearPrivilege(); }); }
        finally { updates.Enqueue(() => Busy = false); }
    }
    private async Task<T> Post<T>(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options:json) };
        if (participantToken.Length > 0) request.Headers.Authorization = new("Bearer", participantToken);
        if (gmToken.Length > 0) request.Headers.Add("X-GM-Session", gmToken);
        using var response = await http.SendAsync(request, stop.Token);
        if (response.StatusCode == HttpStatusCode.Unauthorized) { joined = false; participantToken = gmToken = ""; }
        if (response.StatusCode == HttpStatusCode.Forbidden) gmToken = "";
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(json, stop.Token) ?? throw new InvalidDataException();
    }
    private static Uri RelayOrigin(string endpoint)
    {
        endpoint = string.IsNullOrWhiteSpace(endpoint) ? Configuration.DefaultRelayUrl : endpoint.Trim();
        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Enter a valid HTTPS relay address, or use the default relay.");
        return uri;
    }
    public async Task Browse(string endpoint)
    {
        if (joined) return;
        updates.Enqueue(() => { PublicRooms = []; BrowserStatus = "Looking for tables…"; });
        try
        {
            // Browsing is anonymous; never send room tokens or GM authorization.
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(RelayOrigin(endpoint), "v1/rooms")) { Content = JsonContent.Create(new { }) };
            using var response = await http.SendAsync(request, stop.Token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<RoomsReply>(json, stop.Token) ?? throw new InvalidDataException();
            updates.Enqueue(() => { PublicRooms = result.Rooms; BrowserStatus = result.Rooms.Length == 0 ? "No public tables yet. Create one below." : "Select Join beside a table."; });
        }
        catch { updates.Enqueue(() => BrowserStatus = "Could not load tables. Check the relay, then refresh."); throw; }
    }
    public async Task Join(string endpoint, string name, string code, bool create, bool listed = false, string title = "Dice table")
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter Your Display Name before joining a table.");
        // HttpClient.BaseAddress is immutable after first request; use absolute request paths via a new origin field.
        origin = RelayOrigin(endpoint);
        participantToken = gmToken = ""; sequence = 0;
        var start = Stopwatch.GetTimestamp();
        var result = await PostAt<JoinReply>(create ? "v1/create" : "v1/join", create ? new { name, listed, title } : (object)new { name, room = code.Trim().ToUpperInvariant() });
        Room = result.Room; ParticipantId = result.Participant; participantToken = result.Token;
        SetClock(result.ServerTime, start);
        joined = true;
        updates.Enqueue(() => { Rolls.Clear(); Participants = []; ClearPrivilege(); Status = "Connected"; });
        if (credentialStore.Exists) await RestoreAuthentication();
    }
    private Uri? origin;
    private Task<T> PostAt<T>(string path, object body) => Post<T>(new Uri(origin ?? throw new InvalidOperationException(), path).AbsoluteUri, body);
    private void SetClock(long serverTime, long start)
    {
        Interlocked.Exchange(ref clockServer, serverTime + (long)(Stopwatch.GetElapsedTime(start).TotalMilliseconds / 2));
        Interlocked.Exchange(ref clockStamp, Stopwatch.GetTimestamp());
    }
    public async Task Authenticate(string credential, bool remember = false)
    {
        var result = await PostAt<AuthReply>("v1/auth", new {room = Room, credential});
        gmToken = result.Token;
        updates.Enqueue(() => { CanManage = result.CanManage; gmExpires = result.Expires; AuthenticationStatus = "Authenticated"; Status = "Server authentication accepted"; });
        if (remember)
        {
            try { credentialStore.Save(origin!,credential); }
            catch { updates.Enqueue(() => AuthenticationStatus="Authenticated, but Windows could not save the code."); }
        }
        await LoadModifiers();
    }
    public async Task RestoreAuthentication()
    {
        try
        {
            var saved=credentialStore.Read(origin!);
            if (saved == null) { updates.Enqueue(()=>AuthenticationStatus="No saved code for this relay."); return; }
            await Authenticate(saved);
        }
        catch (HttpRequestException)
        {
            updates.Enqueue(()=> { ClearPrivilege(); AuthenticationStatus="Could not restore access. Retry, or enter a valid code."; });
        }
        catch { updates.Enqueue(()=>AuthenticationStatus="Windows could not read the saved code. Enter it again."); }
    }
    public Task ForgetCredential()
    {
        credentialStore.Forget();
        updates.Enqueue(()=>AuthenticationStatus=CanManage ? "Authenticated; saved code removed." : "Saved code removed.");
        return Task.CompletedTask;
    }
    public async Task RollDice(int count, int sides, string skin)
    {
        // If delivery is uncertain, poll recovers the accepted event. Never auto-submit a new request ID.
        await PostAt<Roll>("v1/roll", new {room = Room, count, sides, skin, requestId = Guid.NewGuid().ToString()});
    }
    public async Task SetModifier(string id, int value)
    {
        await PostAt<JsonElement>("v1/gm/set", new {room = Room, participant = id, value});
        await LoadModifiers();
    }
    public async Task LoadModifiers()
    {
        var value = await PostAt<ModifierReply>("v1/gm/modifiers", new {room = Room});
        updates.Enqueue(() => { if (CanManage) Modifiers = value.Modifiers; });
    }
    public async Task ClearModifiers()
    {
        await PostAt<JsonElement>("v1/gm/clear", new {room = Room}); await LoadModifiers();
    }
    public async Task Logout()
    {
        try { await PostAt<JsonElement>("v1/gm/logout", new {room = Room}); }
        finally { gmToken = ""; updates.Enqueue(()=> { ClearPrivilege(); AuthenticationStatus="Session ended"; }); }
    }
    public async Task Leave()
    {
        try { await PostAt<JsonElement>("v1/leave", new {room = Room}); }
        finally { joined = false; participantToken = gmToken = ""; updates.Enqueue(() => { Room = ""; Participants = []; Rolls.Clear(); ClearPrivilege(); Status = "Not connected"; }); }
    }
    private async Task PollLoop()
    {
        var failures = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(failures == 0 ? 1000 : Math.Min(8000, 500 * (1 << Math.Min(failures, 4))), stop.Token);
                await gate.WaitAsync(stop.Token);
                try
                {
                    if (!joined) continue;
                    var start = Stopwatch.GetTimestamp();
                    var result = await PostAt<PollReply>("v1/poll", new {room = Room, after = sequence});
                    SetClock(result.ServerTime, start);
                    sequence = result.Sequence; failures = 0;
                    if (!result.CanManage) gmToken = "";
                    updates.Enqueue(() =>
                    {
                        // Storage row order can change as participants poll. Keep
                        // the roster stable, including players sharing a name.
                        Participants = result.Participants
                            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(p => p.Id, StringComparer.Ordinal)
                            .ToArray();
                        CanManage = result.CanManage; gmExpires = result.GmExpires;
                        if (!CanManage) Modifiers.Clear();
                        foreach (var roll in result.Rolls) if (Valid(roll) && Rolls.All(r => r.Id != roll.Id)) Rolls.Add(roll);
                        if (Rolls.Count > 100) Rolls.RemoveRange(0, Rolls.Count - 100);
                        Status = "Connected";
                    });
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (HttpRequestException e) when (e.StatusCode is { } status && (int)status >= 500)
            { failures++; updates.Enqueue(() => { ClearPrivilege(); Status = $"Relay unavailable (HTTP {(int)status}); retrying…"; }); }
            catch { failures++; updates.Enqueue(() => { ClearPrivilege(); Status = joined ? "Connection interrupted; retrying…" : "Session ended. Join the room again."; }); }
        }
    }
    public static bool Valid(Roll r) => r.Count is >= 1 and <= 20 && new[] {4,6,8,10,12,20,100}.Contains(r.Sides) && r.Faces.Length == r.Count && r.Faces.All(x => x >= 1 && x <= r.Sides) && r.Total == r.Faces.Sum() && r.DurationMs is >= 500 and <= 10000;
    public void Dispose()
    {
        stop.Cancel(); http.Dispose(); participantToken = gmToken = "";
        // Cancellation completes both the polling loop and any queued action without touching UI.
        _ = pollTask.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
