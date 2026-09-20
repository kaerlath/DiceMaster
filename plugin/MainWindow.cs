using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DiceMaster;

public sealed class MainWindow : Window
{
    private readonly Configuration config;
    private readonly RelayClient relay;
    private readonly Action save, showDice;
    private string endpoint, name, room = "", credential = "";
    private int count = 1, selected = 5;
    private bool rememberAuthentication;
    private bool publicTable = true, nameEdited;
    private string tableTitle = "", browserEndpoint = "";
    private readonly int[] sides = [4,6,8,10,12,20,100];
    private readonly Dictionary<string,int> drafts = [];
    public MainWindow(Configuration config, RelayClient relay, Action save, Action showDice) : base("DiceMaster")
    {
        this.config=config; this.relay=relay; this.save=save; this.showDice=showDice;
        endpoint=config.RelayUrl; name=config.DisplayName;
        Size=new Vector2(610,660); SizeCondition=ImGuiCond.FirstUseEver;
        SizeConstraints=new WindowSizeConstraints { MinimumSize=new Vector2(540,460), MaximumSize=new Vector2(1200,1200) };
    }
    public override void OnClose() { credential=""; drafts.Clear(); }
    public override void Draw()
    {
        Appearance.Heading("D I C E M A S T E R","A shared table. A little chance.");
        ImGui.TextColored(relay.Joined ? new Vector4(.45f,.83f,.65f,1) : Appearance.Muted,relay.Status);
        if (relay.ConnectionPaused)
        {
            ImGui.TextWrapped(relay.PauseMessage);
            if (!relay.UpdateRequired && ImGui.Button("Resume connection")) relay.ResumeConnection();
        }
        if (!relay.CanManage) drafts.Clear();
        if (!ImGui.BeginTabBar("Workspace")) return;
        if (ImGui.BeginTabItem("Table")) { DrawTable(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Appearance")) { DrawAppearance(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Table Style")) { DrawTableStyle(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Connection")) { DrawConnection(); ImGui.EndTabItem(); }
        // No GM tab or placeholder is rendered without server-confirmed capability.
        if (relay.CanManage && ImGui.BeginTabItem("GM Controls")) { DrawGm(); ImGui.EndTabItem(); }
        ImGui.EndTabBar();
    }
    private void DrawTable()
    {
        ImGui.Spacing(); ImGui.BeginDisabled(relay.Busy);
        if (!relay.Joined)
        {
            Appearance.Heading("Take a seat","Join a public table, create your own, or use a room code.");
            if (!nameEdited && string.IsNullOrWhiteSpace(config.DisplayName) && Plugin.PlayerState.IsLoaded)
                name = Plugin.PlayerState.CharacterName.Trim();
            ImGui.TextUnformatted("Your Display Name");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##name","Character name or a name of your choice",ref name,48)) nameEdited = true;
            var missingName = string.IsNullOrWhiteSpace(name);
            if (missingName) ImGui.TextWrapped("Enter a name above to create or join a table.");
            var effectiveEndpoint = string.IsNullOrWhiteSpace(endpoint) ? Configuration.DefaultRelayUrl : endpoint.Trim();
            if (browserEndpoint != effectiveEndpoint && !relay.Busy)
            { browserEndpoint = effectiveEndpoint; relay.Run(() => relay.Browse(effectiveEndpoint)); }
            ImGui.TextUnformatted("Public tables"); ImGui.SameLine();
            if (ImGui.SmallButton("Refresh tables")) relay.Run(() => relay.Browse(effectiveEndpoint));
            ImGui.TextWrapped(relay.BrowserStatus);
            ImGui.BeginChild("public-tables",new Vector2(0,130),true);
            foreach (var table in relay.PublicRooms)
            {
                ImGui.PushID(table.Code);
                ImGui.BeginDisabled(missingName || table.Participants >= table.Capacity);
                if (ImGui.SmallButton("Join")) { room = table.Code; Connect(false); }
                ImGui.EndDisabled(); ImGui.SameLine();
                ImGui.TextUnformatted($"{table.Title}  ({table.Participants}/{table.Capacity})");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(table.Code);
                ImGui.PopID();
            }
            ImGui.EndChild();
            ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##table-title","New table name (optional)",ref tableTitle,64);
            ImGui.Checkbox("List my new table publicly",ref publicTable);
            ImGui.BeginDisabled(missingName);
            if (ImGui.Button("Create a room",new Vector2(-1,38))) Connect(true);
            ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##room","Room code",ref room,10);
            ImGui.BeginDisabled(room.Trim().Length != 10);
            if (ImGui.Button("Join room",new Vector2(-1,38))) Connect(false);
            ImGui.EndDisabled(); ImGui.EndDisabled();
        }
        else
        {
            ImGui.TextUnformatted($"ROOM  {relay.Room}");
            ImGui.SameLine(); if (ImGui.SmallButton("Copy")) ImGui.SetClipboardText(relay.Room);
            ImGui.Spacing(); ImGui.TextColored(Appearance.Muted,"CHOOSE YOUR DICE");
            var width=Math.Max(38,(ImGui.GetContentRegionAvail().X-6*10)/7);
            for (var i=0;i<sides.Length;i++)
            {
                if (i>0) ImGui.SameLine();
                var active=i==selected;
                if (active) ImGui.PushStyleColor(ImGuiCol.Button,new Vector4(DiceSkin.Selected(config).Edge*.4f,1));
                if (ImGui.Button($"d{sides[i]}",new Vector2(width,42))) selected=i;
                if (active) ImGui.PopStyleColor();
            }
            ImGui.SetNextItemWidth(130); ImGui.InputInt("Quantity",ref count); count=Math.Clamp(count,1,20);
            if (ImGui.Button($"ROLL {count}d{sides[selected]}",new Vector2(-1,50)))
            { var n=count; var s=sides[selected]; var skin=DiceSkin.Selected(config).Id; showDice(); relay.Run(()=>relay.RollDice(n,s,skin)); }
            if (ImGui.Button("Open Dice Window",new Vector2(-1,34))) showDice();
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextColored(Appearance.Muted,$"AT THE TABLE  /  {relay.Participants.Length}");
            foreach (var p in relay.Participants)
            {
                ImGui.Bullet(); ImGui.SameLine(); ImGui.TextUnformatted(p.Name);
                if (p.Id==relay.ParticipantId) { ImGui.SameLine(); ImGui.TextColored(Appearance.Muted,"you"); }
            }
        }
        ImGui.EndDisabled();
    }
    private void DrawTableStyle()
    {
        ImGui.Spacing();
        Appearance.Heading("Your dice table", "Only you see this setting. Everyone still sees the roller's dice.");
        if (ImGui.BeginCombo("Table finish",TableFinish.Selected(config).Name))
        {
            foreach (var finish in TableFinish.All)
                if (ImGui.Selectable(finish.Name,finish.Id==config.TableFinishId)) { config.TableFinishId=finish.Id; save(); }
            ImGui.EndCombo();
        }
        bool decoration=config.TrayDecoration;
        if (ImGui.Checkbox("Decorative edge inlay",ref decoration)) { config.TrayDecoration=decoration; save(); }
        DiceWindow.Preview(config);
        ImGui.TextWrapped("Preview with your selected dice. Textures stay subtle and decorations stay at the edges.");
        if (ImGui.Button("Open Dice Window",new Vector2(-1,38))) showDice();
    }
    private void DrawAppearance()
    {
        ImGui.Spacing(); Appearance.Heading("Your dice collection","Everyone sees your selected finish when you roll.");
        ImGui.BeginChild("skin-list",new Vector2(0,Math.Max(180,ImGui.GetContentRegionAvail().Y-135)),true);
        for (var i=0;i<DiceSkin.All.Length;i++)
        {
            var skin=DiceSkin.All[i]; ImGui.PushID(i);
            var texture=skin.Texture == null ? null : Plugin.Textures.GetFromFile(System.IO.Path.Combine(Plugin.Interface.AssemblyLocation.DirectoryName!,"Assets","Skins",skin.Texture+".png")).GetWrapOrDefault();
            if (texture != null) ImGui.Image(texture.Handle,new Vector2(30,30),Vector2.Zero,Vector2.One,new Vector4(skin.Tint,1));
            else ImGui.ColorButton("##swatch",new Vector4(skin.Face,1),ImGuiColorEditFlags.NoTooltip,new Vector2(30,30));
            ImGui.SameLine();
            if (ImGui.Selectable(skin.Name,config.Skin==i,ImGuiSelectableFlags.None,new Vector2(0,30))) { config.Skin=i; save(); }
            ImGui.TextColored(Appearance.Muted,skin.Description); ImGui.Spacing(); ImGui.PopID();
        }
        ImGui.EndChild();
        bool decoration=config.TrayDecoration;
        if (ImGui.Checkbox("Decorative tray inlay",ref decoration)) { config.TrayDecoration=decoration; save(); }
        if (ImGui.Button("Preview in Dice Window",new Vector2(-1,38))) showDice();
        ImGui.TextWrapped("Your selection is saved for future rolls. Each player's dice keep their own finish.");
    }
    private void DrawConnection()
    {
        ImGui.Spacing(); ImGui.BeginDisabled(relay.Busy);
        if (relay.Joined)
        {
            ImGui.TextUnformatted($"Connected to {relay.Room}");
            if (ImGui.Button("Leave room")) { credential=""; browserEndpoint=""; relay.Run(relay.Leave); }
        }
        else
        {
            ImGui.TextColored(Appearance.Muted,"RELAY ADDRESS");
            ImGui.SetNextItemWidth(-1); ImGui.InputText("##endpoint",ref endpoint,256);
            if (ImGui.Button("Use default relay")) { endpoint=Configuration.DefaultRelayUrl; config.RelayUrl=endpoint; save(); }
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextUnformatted("Server Authentication");
        ImGui.TextWrapped("Optional. Leave blank unless you were issued a code.");
        ImGui.TextWrapped(relay.AuthenticationStatus);
        ImGui.TextWrapped(relay.CanManage ? relay.AuthenticationStatus : relay.HasSavedCredential ? "Code saved securely. Join a room to restore access, or use Restore below." : "Not authenticated. Enter your issued code after joining a room.");
        ImGui.Checkbox("Remember on this device",ref rememberAuthentication);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Encrypts the code for your Windows account, outside the plugin install folder. Used only with this relay after you join a room.");
        ImGui.BeginDisabled(!relay.Joined);
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##auth","Access credential",ref credential,110,ImGuiInputTextFlags.Password);
        if (ImGui.Button("Authenticate",new Vector2(-1,38))) { var value=credential; var remember=rememberAuthentication; credential=""; relay.Run(()=>relay.Authenticate(value,remember)); }
        if (relay.HasSavedCredential && ImGui.Button("Restore saved authentication",new Vector2(-1,34))) relay.Run(relay.RestoreAuthentication);
        ImGui.TextWrapped("The code field clears after use. Your access status is shown above.");
        ImGui.EndDisabled();
        if (relay.HasSavedCredential && ImGui.Button("Forget saved code")) relay.Run(relay.ForgetCredential);
        ImGui.EndDisabled();
    }
    private void DrawGm()
    {
        ImGui.Spacing(); Appearance.Heading("GM Controls","Private assignments for this room session.");
        ImGui.TextWrapped("Modifier per die: the full value is added to every die, within that die's limits.");
        ImGui.BeginDisabled(relay.Busy);
        if (ImGui.Button("Refresh")) { drafts.Clear(); relay.Run(relay.LoadModifiers); }
        ImGui.SameLine(); if (ImGui.Button("Clear all")) { drafts.Clear(); relay.Run(relay.ClearModifiers); }
        ImGui.Spacing();
        foreach (var p in relay.Participants)
        {
            ImGui.PushID(p.Id); ImGui.TextUnformatted(p.Name);
            ImGui.SameLine(); ImGui.TextColored(Appearance.Muted,p.Id[..8]);
            int value=drafts.GetValueOrDefault(p.Id,relay.Modifiers.GetValueOrDefault(p.Id));
            ImGui.SetNextItemWidth(160);
            if (ImGui.InputInt("##modifier",ref value)) drafts[p.Id]=value=Math.Clamp(value,-2000,2000);
            ImGui.SameLine(); if (ImGui.Button("Apply")) { var id=p.Id; var v=value; relay.Run(()=>relay.SetModifier(id,v)); }
            ImGui.Separator(); ImGui.PopID();
        }
        if (ImGui.Button("End authenticated session")) relay.Run(relay.Logout);
        ImGui.EndDisabled();
    }
    private void Connect(bool create)
    {
        endpoint=string.IsNullOrWhiteSpace(endpoint) ? Configuration.DefaultRelayUrl : endpoint.Trim();
        config.RelayUrl=endpoint; config.DisplayName=name.Trim(); save();
        var url=endpoint; var display=name; var code=room;
        var listed=publicTable; var title=string.IsNullOrWhiteSpace(tableTitle) ? "Dice table" : tableTitle.Trim();
        relay.Run(()=>relay.Join(url,display,code,create,listed,title)); showDice();
    }
}

