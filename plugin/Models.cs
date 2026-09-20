using Dalamud.Configuration;

namespace DiceMaster;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public const string DefaultRelayUrl = "https://dicemaster-relay.kaerlath.workers.dev";
    public string RelayUrl { get; set; } = DefaultRelayUrl;
    public string DisplayName { get; set; } = "";
    public int Skin { get; set; }
    public string TableFinishId { get; set; } = "midnight-velvet";
    public bool TrayDecoration { get; set; } = true;
    // No credentials, tokens or modifiers in configuration. Optional remembered
    // credentials use a separate Windows-encrypted, relay-bound store.
}
public sealed record Participant(string Id, string Name);
public sealed record PublicRoom(string Code, string Title, int Participants, int Capacity);
public sealed record RoomsReply(PublicRoom[] Rooms);
public sealed record Roll(string Id, long Sequence, string Participant, string Name, int Count, int Sides,
    int[] Faces, int Total, uint AnimationSeed, long StartsAt, int DurationMs, string Skin = "aether-teal");
public sealed record JoinReply(string Room, string Participant, string Token, long ServerTime);
public sealed record AuthReply(string Token, long Expires, bool CanManage);
public sealed record PollReply(long ServerTime, long Sequence, Participant[] Participants, Roll[] Rolls, bool CanManage, long GmExpires);
public sealed record ModifierReply(Dictionary<string, int> Modifiers);

