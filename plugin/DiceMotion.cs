using System.Numerics;

namespace DiceMaster;

// Version 1 motion profile, derived only from the independent public animation seed.
// Pure absolute-time evaluation: frame rate and polling cadence do not alter paths.
public readonly record struct DiceMotion(float DelayMs, float DurationMs, float Phase, float Turns, float Bounces, float Drift)
{
    public static DiceMotion For(uint seed,int index,int visible,int durationMs)
    {
        var state=Mix(seed+unchecked((uint)index*2654435761u));
        float Next() { state=Mix(state+0x9e3779b9u); return (state>>8)/16777216f; }
        var delay=Next()*Math.Min(300,durationMs*.1f);
        var finish=index==(int)(seed%(uint)visible) ? durationMs : durationMs*(.58f+.40f*Next());
        return new(delay,finish-delay,Next()*MathF.Tau,1.8f+Next()*3.8f,2.4f+Next()*3.2f,.16f+Next()*.15f);
    }
    public float Progress(float elapsedMs) => Math.Clamp((elapsedMs-DelayMs)/DurationMs,0,1);
    public Vector2 Offset(float t,Vector2 cell)
    {
        var rest=1-t;
        return new(MathF.Sin(Phase+t*MathF.PI*1.6f)*cell.X*Drift*rest*rest,
            -MathF.Abs(MathF.Sin(t*MathF.PI*Bounces))*cell.Y*.28f*rest);
    }
    public Quaternion Rotation(float t,Quaternion landing)
    {
        var remaining=(1-t)*(1-t);
        var angle=remaining*Turns*MathF.Tau;
        return Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(angle,angle*(.7f+Phase/MathF.Tau),angle*.55f)*landing);
    }
    private static uint Mix(uint x)
    {
        unchecked { x^=x>>16; x*=0x7feb352d; x^=x>>15; x*=0x846ca68b; return x^(x>>16); }
    }
}
