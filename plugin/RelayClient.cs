using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;

namespace DiceMaster;

// Network work never touches ImGui. Immutable results enter the UI through a queue.
public sealed class RelayClient : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim gate = new(1);
    private readonly NetworkBudget budget = new();
    private long lastAction = -10000, lastBrowse = -10000;
    public bool ConnectionPaused => budget.Paused;
    public bool UpdateRequired => budget.UpdateRequired;
    public string PauseMessage => budget.Message;
    public void ResumeConnection() { if (budget.Resume()) Status = "Ready to reconnect. If not at a table, refresh or join again."; }
    private readonly ConcurrentQueue<Action> updates = new();
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private string participantToken = "", gmToken = "";
    private long gmExpires;
    private long clockServer, clockStamp = Stopwatch.GetTimestamp();
    private volatile bool joined;
    private readonly Task pollTask;
    private ClientWebSocket? liveSocket;
    private void ResetStream() { try { liveSocket?.Abort(); } catch (ObjectDisposedException) { } }
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
        if (Busy || Environment.TickCount64-lastAction<1000) return;
        lastAction=Environment.TickCount64;
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
        catch (NetworkPausedException) { updates.Enqueue(() => { Status = budget.Message; ClearPrivilege(); }); }
        catch (OperationCanceledException) { updates.Enqueue(() => Status = "The relay took too long to respond. Please retry."); }
        catch (ArgumentException e) { updates.Enqueue(() => Status = e.Message); }
        catch (HttpRequestException e)
        {
            updates.Enqueue(() => { Status = budget.Paused ? budget.Message : e.StatusCode == HttpStatusCode.Forbidden ? "Authorization required or expired." : "Request failed. Check the relay and room connection."; ClearPrivilege(); });
        }
        catch { updates.Enqueue(() => { Status = "Could not complete the request."; ClearPrivilege(); }); }
        finally { updates.Enqueue(() => Busy = false); }
    }
    private async Task<T> Post<T>(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options:json) };
        if (participantToken.Length > 0) request.Headers.Authorization = new("Bearer", participantToken);
        if (gmToken.Length > 0) request.Headers.Add("X-GM-Session", gmToken);
        using var response = await SendHttp(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized) { joined = false; participantToken = gmToken = ""; }
        if (response.StatusCode == HttpStatusCode.Forbidden) gmToken = "";
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(json, stop.Token) ?? throw new InvalidDataException();
    }
    private async Task<HttpResponseMessage> SendHttp(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("X-DiceMaster-Protocol","2");
        budget.Request();
        try
        {
            var response=await http.SendAsync(request,stop.Token);
            var retry=response.Headers.RetryAfter;
            budget.Response((int)response.StatusCode,retry?.Delta ?? (retry?.Date is { } date ? date-DateTimeOffset.UtcNow : null));
            return response;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
        catch { budget.TransportFailure(); throw; }
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
        if (Environment.TickCount64-lastBrowse<10000) return;
        lastBrowse=Environment.TickCount64;
        updates.Enqueue(() => { PublicRooms = []; BrowserStatus = "Looking for tables…"; });
        try
        {
            // Browsing is anonymous; never send room tokens or GM authorization.
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(RelayOrigin(endpoint), "v1/rooms")) { Content = JsonContent.Create(new { }) };
            using var response = await SendHttp(request);
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
        participantToken = gmToken = "";
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
        gmToken = result.Token; ResetStream();
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
        // If delivery is uncertain, the stream or reconnect snapshot recovers the accepted event. Never auto-submit a new request ID.
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
        finally { ResetStream(); gmToken = ""; updates.Enqueue(()=> { ClearPrivilege(); AuthenticationStatus="Session ended"; }); }
    }
    public async Task Leave()
    {
        try { await PostAt<JsonElement>("v1/leave", new {room = Room}); }
        finally { joined = false; ResetStream(); participantToken = gmToken = ""; updates.Enqueue(() => { Room = ""; Participants = []; Rolls.Clear(); ClearPrivilege(); Status = "Not connected"; }); }
    }
    private void ApplySnapshot(PollReply result, string session, string auth)
    {
        updates.Enqueue(() =>
        {
            if (!joined || participantToken != session || gmToken != auth) return;
            Participants = result.Participants.OrderBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).ThenBy(p=>p.Id,StringComparer.Ordinal).ToArray();
            CanManage=result.CanManage; gmExpires=result.GmExpires;
            if (!CanManage) Modifiers.Clear();
            foreach (var roll in result.Rolls) if (Valid(roll) && Rolls.All(r=>r.Id!=roll.Id)) Rolls.Add(roll);
            if (Rolls.Count>100) Rolls.RemoveRange(0,Rolls.Count-100);
            Status="Connected · live updates";
        });
    }
    private async Task PollLoop()
    {
        var failures=0;
        while (!stop.IsCancellationRequested)
        {
            string session="", auth="";
            try
            {
                await Task.Delay(failures==0 ? 500 : Math.Min(60000,5000*(1<<Math.Min(failures-1,4)))+Random.Shared.Next(2000),stop.Token);
                if (budget.Paused) continue;
                using var socket=new ClientWebSocket();
                await gate.WaitAsync(stop.Token);
                try
                {
                    if (!joined) continue;
                    budget.Connect();
                    session=participantToken; auth=gmToken;
                    // One catch-up request on connect/reconnect validates the session and synchronizes time.
                    var start=Stopwatch.GetTimestamp();
                    var snapshot=await PostAt<PollReply>("v1/poll",new {room=Room,after=0});
                    SetClock(snapshot.ServerTime,start);
                    socket.Options.SetRequestHeader("Authorization","Bearer "+session);
                    socket.Options.SetRequestHeader("X-DiceMaster-Protocol","2");
                    if (auth.Length>0) socket.Options.SetRequestHeader("X-GM-Session",auth);
                    socket.Options.CollectHttpResponseDetails=true;
                    socket.Options.KeepAliveInterval=TimeSpan.FromSeconds(30);
                    socket.Options.KeepAliveTimeout=TimeSpan.FromSeconds(20);
                    var uri=new UriBuilder(new Uri(origin!,"v1/connect")) { Scheme="wss", Port=origin!.IsDefaultPort ? -1 : origin.Port, Query="room="+Uri.EscapeDataString(Room) };
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    budget.Request();
                    try { await socket.ConnectAsync(uri.Uri,timeout.Token); }
                    catch { budget.Response((int)socket.HttpStatusCode); budget.TransportFailure(); throw; }
                    liveSocket=socket;
                    ApplySnapshot(snapshot,session,auth);
                }
                finally { gate.Release(); }
                var buffer=new byte[8192];
                while (joined && participantToken==session && gmToken==auth && socket.State==WebSocketState.Open)
                {
                    using var message=new MemoryStream();
                    WebSocketReceiveResult part;
                    do
                    {
                        part=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),stop.Token);
                        if (part.MessageType==WebSocketMessageType.Close) throw new IOException("Stream closed");
                        if (part.MessageType!=WebSocketMessageType.Text || message.Length+part.Count>524288) throw new InvalidDataException();
                        message.Write(buffer,0,part.Count);
                    } while (!part.EndOfMessage);
                    var result=JsonSerializer.Deserialize<PollReply>(message.ToArray(),json) ?? throw new InvalidDataException();
                    ApplySnapshot(result,session,auth);
                    // A brief successful snapshot must not reset the reconnect budget.
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch
            {
                if (session==participantToken && auth==gmToken)
                {
                    failures++;
                    updates.Enqueue(()=> { if (session!=participantToken || auth!=gmToken) return; ClearPrivilege(); Status=budget.Paused ? budget.Message : joined ? "Live connection interrupted; reconnecting…" : "Session ended. Join a table again."; });
                }
            }
            finally { liveSocket=null; }
        }
    }
    public static bool Valid(Roll r) => r.Count is >= 1 and <= 20 && new[] {4,6,8,10,12,20,100}.Contains(r.Sides) && r.Faces.Length == r.Count && r.Faces.All(x => x >= 1 && x <= r.Sides) && r.Total == r.Faces.Sum() && r.DurationMs is >= 500 and <= 10000;
    public void Dispose()
    {
        stop.Cancel(); ResetStream(); http.Dispose(); participantToken = gmToken = "";
        // Cancellation completes both the polling loop and any queued action without touching UI.
        _ = pollTask.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}




