using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DiceMaster;

public sealed class DiceWindow : Window
{
    private readonly RelayClient relay;
    private readonly Configuration config;
    public DiceWindow(RelayClient relay, Configuration config) : base("Dice Window — DiceMaster")
    {
        this.relay = relay; this.config = config; Size = new Vector2(820,680); SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(400,400), MaximumSize = new Vector2(1600,1400) };
    }
    public override void Draw() => DrawContents(false);
    internal void DrawContents(bool embedded)
    {
        var now = relay.ServerNow;
        var roll = relay.Rolls.LastOrDefault(r => r.StartsAt <= now);
        var skin = roll == null ? DiceSkin.Selected(config) : DiceSkin.FromId(roll.Skin);
        if (embedded) ImGui.TextColored(Appearance.Muted,skin.Name);
        else Appearance.Heading("T H E   D I C E   T A B L E",skin.Name + (relay.Joined ? $"  /  {relay.Room}" : "  /  Collection preview"));
        ImGui.TextUnformatted(roll == null ? "The table is ready." : $"{roll.Name}  ·  {roll.Count}d{roll.Sides}");
        var origin = ImGui.GetCursorScreenPos();
        var area = new Vector2(Math.Max(280,ImGui.GetContentRegionAvail().X),embedded ? Math.Clamp(ImGui.GetWindowHeight()*.45f,160,340) : Math.Clamp(ImGui.GetContentRegionAvail().Y*.65f,240,700));
        var draw = ImGui.GetWindowDrawList();
        var finish=TableFinish.Selected(config);
        TableFinish.Draw(draw,origin,area,finish,config.TrayDecoration);
        draw.PushClipRect(origin,origin+area,true);
        if (roll == null)
        {
            var mesh=DieMesh.For(20);
            DrawDie(draw,mesh,mesh.Landing(19),origin+area/2,Math.Min(area.X,area.Y)*.28f,false,false,skin);
            var note="d20 / skin preview";
            draw.AddText(origin+new Vector2(area.X/2,area.Y-34)-new Vector2(ImGui.CalcTextSize(note).X/2,0),DiceSkin.Color(finish.Text),note);
        }
        if (roll != null)
        {
            var visible = roll.Sides == 100 ? roll.Count*2 : roll.Count;
            var cols = Math.Min(visible,Math.Max(1,(int)Math.Ceiling(Math.Sqrt(visible*area.X/area.Y))));
            if (roll.Sides == 100) cols=Math.Min(visible,Math.Max(2,cols+(cols%2)));
            var rows = (visible+cols-1)/cols;
            var cell = new Vector2(area.X/cols,area.Y/rows);
            for (var i=0;i<visible;i++)
            {
                var face = roll.Faces[roll.Sides == 100 ? i/2 : i];
                var percentile = roll.Sides == 100;
                var display = percentile ? (i%2 == 0 ? (face%100)/10 : face%10) : face;
                var mesh = DieMesh.For(percentile ? 10 : roll.Sides);
                var finalIndex = percentile ? display : display-1;
                var motion = DiceMotion.For(roll.AnimationSeed,i,visible,roll.DurationMs);
                var t = motion.Progress(now-roll.StartsAt);
                var center = origin+new Vector2((i%cols+.5f)*cell.X,(i/cols+.5f)*cell.Y);
                center += motion.Offset(t,cell);
                var landing = mesh.Landing(finalIndex);
                var q = motion.Rotation(t,landing);
                var radius = Math.Min(120,Math.Min(cell.X,cell.Y)*.32f);
                var shadow = center+new Vector2(4,radius*.65f);
                for (var k=0;k<24;k++)
                {
                    var a = k*MathF.Tau/24; var b = (k+1)*MathF.Tau/24;
                    draw.AddTriangleFilled(shadow,shadow+new Vector2(MathF.Cos(a)*radius*.8f,MathF.Sin(a)*radius*.28f),shadow+new Vector2(MathF.Cos(b)*radius*.8f,MathF.Sin(b)*radius*.28f),0x66000000);
                }
                DrawDie(draw,mesh,q,center,radius,percentile && i%2 == 0,percentile,skin);
            }
        }
        draw.PopClipRect(); ImGui.Dummy(area);
        if (roll != null)
        {
            if (now >= roll.StartsAt+roll.DurationMs)
            {
                ImGui.TextColored(new Vector4(skin.Edge,1),$"TOTAL   {roll.Total}");
                ImGui.SameLine(); ImGui.TextWrapped(string.Join(" + ",roll.Faces));
            }
            else ImGui.TextUnformatted("Rolling…");
            if (roll.Sides == 100) ImGui.TextUnformatted("Percentile pairs: tens + units; 00 / 0 is 100.");
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.TextColored(Appearance.Muted,"RECENT ROLLS");
        if (ImGui.BeginTable("history",4,ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Time",ImGuiTableColumnFlags.WidthFixed,ImGui.CalcTextSize("00:00:00").X+12);
            ImGui.TableSetupColumn("Player",ImGuiTableColumnFlags.WidthStretch,2);
            ImGui.TableSetupColumn("Dice"); ImGui.TableSetupColumn("Total");
            ImGui.TableHeadersRow();
            foreach (var r in relay.Rolls.AsEnumerable().Reverse().Where(r => r.StartsAt+r.DurationMs <= now).Take(12))
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.TextUnformatted(RollTime.Format(r.StartsAt));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(RollTime.Detail(r.StartsAt));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(r.Name);
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{r.Count}d{r.Sides}");
                ImGui.TableNextColumn(); ImGui.TextColored(new Vector4(skin.Edge,1),r.Total.ToString());
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(string.Join(" + ",r.Faces)+"\n"+DiceSkin.FromId(r.Skin).Name);
            }
            ImGui.EndTable();
        }
    }
    internal static void Preview(Configuration config)
    {
        var area=new Vector2(Math.Max(280,ImGui.GetContentRegionAvail().X),220);
        var origin=ImGui.GetCursorScreenPos();
        var draw=ImGui.GetWindowDrawList();
        var finish=TableFinish.Selected(config);
        TableFinish.Draw(draw,origin,area,finish,config.TrayDecoration);
        draw.PushClipRect(origin,origin+area,true);
        var mesh=DieMesh.For(20);
        DrawDie(draw,mesh,mesh.Landing(19),origin+area/2,72,false,false,DiceSkin.Selected(config));
        draw.PopClipRect();ImGui.Dummy(area);
    }
    private static void DrawDie(ImDrawListPtr draw,DieMesh mesh,Quaternion rotation,Vector2 center,float radius,bool tens,bool zeroBased,DiceSkin skin)
    {
        var texture=skin.Texture == null ? null : Plugin.Textures.GetFromFile(System.IO.Path.Combine(Plugin.Interface.AssemblyLocation.DirectoryName!,"Assets","Skins",skin.Texture+".png")).GetWrapOrDefault();
        var points = mesh.Vertices.Select(v => Vector3.Transform(v,rotation)).ToArray();
        foreach (var entry in mesh.Faces.Select((f,i) => (f,i,z:f.Average(j => points[j].Z))).OrderBy(f => f.z))
        {
            var f = entry.f;
            var normal = Vector3.Normalize(Vector3.Cross(points[f[1]]-points[f[0]],points[f[2]]-points[f[0]]));
            if (normal.Z <= .015f) continue;
            var projected = f.Select(i => center+new Vector2(points[i].X,-points[i].Y)*radius).ToArray();
            var illumination=Math.Max(0,Vector3.Dot(normal,Vector3.Normalize(new Vector3(-.5f,.8f,1))));
            var light=.36f+.64f*illumination;
            var highlight=MathF.Pow(illumination,18)*skin.Shine;
            var color=DiceSkin.Color(Vector3.Clamp(skin.Face*light+new Vector3(highlight),Vector3.Zero,Vector3.One));
            if (texture == null)
            {
                for (var i=1;i<projected.Length-1;i++) draw.AddTriangleFilled(projected[0],projected[i],projected[i+1],color);
            }
            else
            {
                var uv=FaceTextureCoordinates.For(mesh,f);
                var tint=DiceSkin.Color(skin.Tint*(.54f+.46f*illumination));
                for (var i=1;i<projected.Length-1;i++)
                {
                    // Degenerate fourth vertex gives a single textured triangle.
                    draw.AddImageQuad(texture.Handle,projected[0],projected[i],projected[i+1],projected[i+1],uv[0],uv[i],uv[i+1],uv[i+1],tint);
                    if (highlight>.005f) draw.AddTriangleFilled(projected[0],projected[i],projected[i+1],DiceSkin.Color(Vector3.One,highlight*.35f));
                }
            }
            var faceCenter=projected.Aggregate(Vector2.Zero,(sum,v)=>sum+v)/projected.Length;
            for (var i=0;i<projected.Length;i++)
            {
                draw.AddLine(projected[i],projected[(i+1)%projected.Length],DiceSkin.Color(new Vector3(.015f),.85f),3.4f);
                draw.AddLine(projected[i],projected[(i+1)%projected.Length],DiceSkin.Color(skin.Edge),1.4f);
                draw.AddLine(Vector2.Lerp(projected[i],faceCenter,.06f),Vector2.Lerp(projected[(i+1)%projected.Length],faceCenter,.06f),DiceSkin.Color(skin.Edge,.22f),1);
            }
            if (normal.Z > .38f)
            {
                var p = projected.Aggregate(Vector2.Zero,(sum,v) => sum+v)/projected.Length;
                var value = zeroBased ? entry.i : entry.i+1;
                var label = tens ? (value*10).ToString("00") : value.ToString();
                var fontSize=Math.Clamp(radius*.28f*normal.Z,9,30);
                var size=ImGui.CalcTextSize(label)*(fontSize/ImGui.GetFontSize());
                var outline=Vector3.Dot(skin.Ink,new Vector3(.2126f,.7152f,.0722f))>.5f ? Vector3.Zero : Vector3.One;
                foreach(var offset in new[]{new Vector2(-1,0),new Vector2(1,0),new Vector2(0,-1),new Vector2(0,1)})
                    draw.AddText(ImGui.GetFont(),fontSize,p-size/2+offset,DiceSkin.Color(outline,.85f),label);
                draw.AddText(ImGui.GetFont(),fontSize,p-size/2,DiceSkin.Color(skin.Ink),label);
            }
        }
    }
}

