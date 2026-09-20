using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DiceMaster;

public sealed record TableFinish(string Id, string Name, Vector3 Surface, Vector3 Rim, Vector3 Trim, bool Light = false)
{
    public static readonly TableFinish[] All =
    [
        new("classic-felt", "Classic Felt", new(.055f,.18f,.12f),new(.035f,.08f,.06f),new(.65f,.57f,.34f)),
        new("midnight-velvet", "Midnight Velvet", new(.045f,.065f,.115f),new(.025f,.035f,.065f),new(.43f,.52f,.67f)),
        new("burgundy-felt", "Burgundy Felt", new(.20f,.055f,.08f),new(.09f,.025f,.035f),new(.65f,.43f,.34f)),
        new("slate", "Slate", new(.14f,.16f,.18f),new(.06f,.07f,.085f),new(.48f,.53f,.58f)),
        new("dark-walnut", "Dark Walnut", new(.105f,.075f,.055f),new(.23f,.13f,.065f),new(.64f,.43f,.24f)),
        new("parchment", "Parchment", new(.72f,.66f,.54f),new(.27f,.20f,.13f),new(.38f,.29f,.18f),true)
    ];
    public static TableFinish Selected(Configuration config) => All.FirstOrDefault(x=>x.Id==config.TableFinishId) ?? All[1];
    public Vector3 Text => Light ? new(.16f,.12f,.08f) : new(.80f,.83f,.86f);
    public static void Draw(ImDrawListPtr draw, Vector2 origin, Vector2 area, TableFinish finish, bool decorated)
    {
        var rim=finish.Id=="dark-walnut" ? 20f : 12f;
        draw.AddRectFilled(origin,origin+area,DiceSkin.Color(finish.Rim),12);
        if(finish.Id=="dark-walnut")
        {
            // Grain stays on the frame; the rolling surface is plain.
            for(var i=0;i<6;i++)
            {
                var y=3+i*2.6f;
                draw.AddLine(origin+new Vector2(12,y),origin+new Vector2(area.X-12,y+1),DiceSkin.Color(finish.Trim,.12f));
                draw.AddLine(origin+new Vector2(12,area.Y-y),origin+new Vector2(area.X-12,area.Y-y-1),DiceSkin.Color(finish.Trim,.12f));
            }
        }
        draw.AddRectFilled(origin+new Vector2(rim),origin+area-new Vector2(rim),DiceSkin.Color(finish.Surface),7);
        // Fixed, low-contrast flecks: no animated noise, image downloads, or large textures.
        if(finish.Id!="dark-walnut")
        {
            uint seed=7391;
            float Next() { seed=1664525*seed+1013904223;return (seed>>8)/16777216f; }
            for(var i=0;i<420;i++)
            {
                var p=origin+new Vector2(rim+6+Next()*(area.X-2*rim-12),rim+6+Next()*(area.Y-2*rim-12));
                var length=finish.Id=="slate" ? 2.5f : finish.Id=="midnight-velvet" ? 3f : 1.2f;
                draw.AddLine(p,p+new Vector2(length,.5f),DiceSkin.Color(finish.Light?Vector3.Zero:Vector3.One,.028f));
            }
        }
        draw.AddRect(origin+new Vector2(rim-2),origin+area-new Vector2(rim-2),DiceSkin.Color(finish.Trim,.6f),8,ImDrawFlags.None,1);
        if(!decorated)return;
        // Keep embellishment outside the rolling field.
        foreach(var x in new[]{6f,area.X-6})foreach(var y in new[]{6f,area.Y-6})
        {
            var p=origin+new Vector2(x,y);
            draw.AddQuadFilled(p+new Vector2(0,-2),p+new Vector2(2,0),p+new Vector2(0,2),p+new Vector2(-2,0),DiceSkin.Color(finish.Trim,.7f));
        }
    }
}
