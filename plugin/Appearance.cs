using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DiceMaster;

public sealed record DiceSkin(string Name, string Description, Vector3 Face, Vector3 Edge, Vector3 Ink, Vector3 Felt, float Shine, string? Texture = null, string? SkinId = null, Vector3? TextureTint = null)
{
    public string Id => SkinId ?? Texture ?? Name.ToLowerInvariant().Replace(' ','-');
    public Vector3 Tint => TextureTint ?? Vector3.One;
    public static DiceSkin FromId(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];
    public static readonly DiceSkin[] All =
    [
        new("Aether Teal", "Teal enamel and warm gold.", new(.12f,.53f,.64f), new(.89f,.73f,.43f), new(1,.96f,.83f), new(.045f,.10f,.13f), .35f),
        new("Royal Amethyst", "Violet facets and pale silver.", new(.43f,.23f,.66f), new(.77f,.79f,.94f), new(.98f,.95f,1), new(.09f,.065f,.14f), .45f),
        new("Obsidian Gold", "Polished charcoal and bright gold.", new(.13f,.15f,.19f), new(.95f,.73f,.31f), new(1,.87f,.57f), new(.065f,.075f,.095f), .65f),
        new("Ivory Brass", "Warm ivory and antique brass.", new(.88f,.81f,.65f), new(.58f,.39f,.17f), new(.17f,.12f,.065f), new(.11f,.095f,.075f), .20f),
        new("Ember Copper", "Burnished crimson and copper.", new(.64f,.16f,.12f), new(.99f,.62f,.35f), new(1,.92f,.76f), new(.13f,.055f,.045f), .5f),
        new("Moonstone Marble", "Ivory mineral veins and antique gold.", new(.88f,.84f,.73f), new(.66f,.49f,.23f), new(.28f,.18f,.055f), new(.045f,.07f,.11f), .25f,"moonstone-marble"),
        new("Elderwood", "Walnut grain with brass inlays.", new(.33f,.17f,.08f), new(.78f,.59f,.28f), new(1,.88f,.61f), new(.035f,.09f,.065f), .12f,"elderwood"),
        new("Astral Glass", "Violet nebula, stars and luminous facets.", new(.12f,.10f,.36f), new(.50f,.62f,.93f), new(.96f,.96f,1), new(.045f,.05f,.14f), .55f,"astral-glass"),
        new("Obsidian Relic", "Volcanic stone repaired with gold.", new(.10f,.10f,.10f), new(.79f,.60f,.26f), new(1,.85f,.51f), new(.055f,.055f,.065f), .28f,"obsidian-relic"),
        new("Frostbound", "Icy veins with deep blue engraving.", new(.63f,.81f,.94f), new(.68f,.86f,1), new(.025f,.10f,.21f), new(.055f,.10f,.14f), .5f,"frostbound"),
        new("Crimson Velvet", "Burgundy damask and antique copper.", new(.37f,.055f,.085f), new(.83f,.48f,.29f), new(1,.92f,.79f), new(.105f,.035f,.05f), .2f,"crimson-velvet"),
        new("Emerald Marble", "Green mineral veins and warm gold.", new(.2f,.65f,.36f), new(.86f,.72f,.33f), new(.04f,.13f,.07f), new(.03f,.10f,.07f), .35f,"moonstone-marble","emerald-marble",new(.36f,.95f,.55f)),
        new("Rose Quartz", "Blush stone with rose-gold edges.", new(.95f,.55f,.70f), new(.88f,.60f,.48f), new(.25f,.07f,.14f), new(.12f,.045f,.085f), .4f,"moonstone-marble","rose-quartz",new(1,.62f,.82f)),
        new("Sapphire Marble", "Cool blue mineral and silver.", new(.3f,.55f,.95f), new(.7f,.82f,1), new(.035f,.08f,.23f), new(.035f,.06f,.13f), .45f,"moonstone-marble","sapphire-marble",new(.38f,.65f,1)),
        new("Amethyst Ice", "Lavender frost and silver engraving.", new(.65f,.4f,.9f), new(.82f,.7f,1), new(.12f,.04f,.23f), new(.08f,.04f,.13f), .5f,"frostbound","amethyst-ice",new(.95f,.60f,1)),
        new("Jade Frost", "Mint glacial veins and deep green ink.", new(.3f,.9f,.65f), new(.62f,1,.82f), new(.02f,.15f,.10f), new(.025f,.10f,.07f), .5f,"frostbound","jade-frost",new(.55f,1,.68f)),
        new("Honey Amber", "Golden honey stone with bronze trim.", new(.95f,.66f,.2f), new(.78f,.47f,.18f), new(.23f,.10f,.025f), new(.12f,.07f,.03f), .4f,"moonstone-marble","honey-amber",new(1,.75f,.32f)),
        new("Rainbow Opal", "Pearlescent rainbow mineral and pale gold.", new(.9f,.8f,.9f), new(.91f,.80f,.53f), new(.17f,.07f,.23f), new(.07f,.06f,.12f), .55f,"rainbow-opal"),
        new("Prismatic Night", "Rainbow ribbons through dark, gold-veined stone.", new(.14f,.12f,.3f), new(.92f,.73f,.34f), new(1,.95f,.79f), new(.035f,.04f,.10f), .5f,"prismatic-night")
    ];
    public static DiceSkin Selected(Configuration c) => All[Math.Clamp(c.Skin,0,All.Length-1)];
    public static uint Color(Vector3 c,float alpha=1) => ImGui.ColorConvertFloat4ToU32(new(c,alpha));
}

public sealed class Appearance : IDisposable
{
    public static readonly Vector4 Muted = new(.57f,.65f,.73f,1);
    public Appearance(Configuration config)
    {
        var accent=DiceSkin.Selected(config).Edge;
        ImGui.PushStyleColor(ImGuiCol.WindowBg,new Vector4(.045f,.060f,.080f,.98f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg,new Vector4(.065f,.085f,.11f,1));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive,new Vector4(.085f,.11f,.14f,1));
        ImGui.PushStyleColor(ImGuiCol.FrameBg,new Vector4(.10f,.13f,.17f,1));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered,new Vector4(.15f,.19f,.24f,1));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,new Vector4(.19f,.23f,.28f,1));
        ImGui.PushStyleColor(ImGuiCol.Button,new Vector4(.13f,.18f,.23f,1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered,new Vector4(.22f,.29f,.35f,1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,new Vector4(accent*.50f,1));
        ImGui.PushStyleColor(ImGuiCol.Header,new Vector4(accent*.28f,1));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered,new Vector4(accent*.40f,1));
        ImGui.PushStyleColor(ImGuiCol.CheckMark,new Vector4(accent,1));
        ImGui.PushStyleColor(ImGuiCol.Text,new Vector4(.91f,.94f,.96f,1));
        ImGui.PushStyleColor(ImGuiCol.Border,new Vector4(.22f,.28f,.34f,.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,new Vector2(20,18));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(10,7));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,new Vector2(10,10));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding,10f);
    }
    public static void Heading(string title,string subtitle)
    {
        ImGui.TextUnformatted(title); ImGui.TextColored(Muted,subtitle);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
    }
    public void Dispose() { ImGui.PopStyleVar(5); ImGui.PopStyleColor(14); }
}
