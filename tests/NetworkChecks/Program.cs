using DiceMaster;
static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
static void Blocked(Action action) { try { action(); } catch(NetworkPausedException) { return; } throw new Exception("Unexpected network permission"); }
long now=0;
var budget=new NetworkBudget(()=>now);
budget.Request(); budget.Response(500);
for(int i=0;i<10000;i++)Blocked(budget.Request);
Check(!budget.Resume(),"Cooldown bypassed");now=60000;
Blocked(budget.Request); // Time alone never resumes traffic.
Check(budget.Resume(),"Explicit resume failed");budget.Request();
var flap=new NetworkBudget(()=>now);
for(int i=0;i<5;i++){flap.Connect();flap.Request();flap.Request();now+=1000;}
Blocked(flap.Connect);Check(flap.Paused,"Flapping not paused");
now+=299000;Check(!flap.Resume(),"Reconnect cooldown bypassed");
now+=1000;Check(flap.Resume(),"Reconnect cooldown did not expire");flap.Connect();
var throttle=new NetworkBudget(()=>now);
for(int i=0;i<60;i++)throttle.Request();Blocked(throttle.Request);
var unavailable=new NetworkBudget(()=>now);
for(int i=0;i<3;i++)unavailable.TransportFailure();Check(unavailable.Paused,"Network failures not paused");
var retry=new NetworkBudget(()=>now);retry.Response(429,TimeSpan.FromMinutes(10));now+=60000;Check(!retry.Resume(),"Retry-After ignored");now+=540000;Check(retry.Resume(),"Retry-After did not expire");
var clientError=new NetworkBudget(()=>now);clientError.Response(403);Check(!clientError.Paused,"Authorization rejection treated as outage");
// Fleet outage simulation: every client sees one 503, then zero retries even after hours.
int sent=0;
for(int i=0;i<100;i++) {
  var client=new NetworkBudget(()=>now);client.Request();sent++;client.Response(503);
  now+=3600000;Blocked(client.Connect);Blocked(client.Request);
}
Check(sent==100,"Outage traffic grew with time");
Console.WriteLine("Network safeguards passed: 5-attempt reconnect cap, flapping, manual-only recovery, cooldown, Retry-After, 60/min request cap, transport failures, 100-client outage (100 initial requests; zero automatic retries).");
var obsolete=new NetworkBudget(()=>now);obsolete.Response(426);now+=86400000;Check(!obsolete.Resume(),"Obsolete client resumed without updating");Blocked(obsolete.Request);
