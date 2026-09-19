using System.Numerics;
using DiceMaster;

foreach (var sides in new[] {4,6,8,10,12,20})
{
    var mesh = DieMesh.For(sides);
    if (mesh.Faces.Length != sides) throw new Exception($"d{sides}: {mesh.Faces.Length} faces");
    for (var i=0;i<sides;i++)
    {
        var f=mesh.Faces[i];
        var uv=FaceTextureCoordinates.For(mesh,f);
        if (uv.Length!=f.Length || uv.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||p.X<0||p.X>1||p.Y<0||p.Y>1)) throw new Exception("Invalid texture coordinates");
        if (Math.Abs((uv[1].X-uv[0].X)*(uv[2].Y-uv[0].Y)-(uv[1].Y-uv[0].Y)*(uv[2].X-uv[0].X))<.00001f) throw new Exception("Degenerate texture mapping");
        var normal=Vector3.Normalize(Vector3.Cross(mesh.Vertices[f[1]]-mesh.Vertices[f[0]],mesh.Vertices[f[2]]-mesh.Vertices[f[0]]));
        if (Vector3.Dot(Vector3.Transform(normal,mesh.Landing(i)),Vector3.UnitZ)<.999f) throw new Exception($"d{sides} face {i+1} not facing camera");
    }
    Console.WriteLine($"d{sides}: {mesh.Vertices.Length} vertices, {mesh.Faces.Length} faces; all landing orientations correct.");
}

for (uint seed=0;seed<100;seed++) foreach (var visible in new[]{1,2,3,20,40}) foreach (var duration in new[]{2400,2600,5000})
{
    var latest=0f;
    for (int i=0;i<visible;i++)
    {
        var motion=DiceMotion.For(seed,i,visible,duration);
        if (motion!=DiceMotion.For(seed,i,visible,duration)) throw new Exception("Motion is not deterministic");
        if (motion.DurationMs<=0 || motion.DelayMs<0 || motion.DelayMs+motion.DurationMs>duration+.01f) throw new Exception("Invalid motion bounds");
        latest=Math.Max(latest,motion.DelayMs+motion.DurationMs);
        if (motion.Progress(-100)!=0 || motion.Progress(duration)!=1) throw new Exception("Animation endpoints invalid");
        if (motion.Offset(1,new Vector2(100,100))!=Vector2.Zero) throw new Exception("Die does not settle");
        var landing=DieMesh.For(20).Landing(7);
        if (Math.Abs(Quaternion.Dot(motion.Rotation(1,landing),landing))<.9999f) throw new Exception("Motion changed the final face");
    }
    if (Math.Abs(latest-duration)>.01f) throw new Exception("Timeline does not include final die");
}
if (DiceMotion.For(1,0,2,3000)==DiceMotion.For(1,1,2,3000)) throw new Exception("Identical per-die motion");
Console.WriteLine("Motion: deterministic profiles, varied per-die timing, bounded duration and correct final orientation passed.");
