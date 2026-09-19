using System.Numerics;

namespace DiceMaster;

public sealed record DieMesh(Vector3[] Vertices, int[][] Faces)
{
    private static readonly Dictionary<int, DieMesh> Cache = [];
    public static DieMesh For(int sides)
    {
        if (Cache.TryGetValue(sides, out var mesh)) return mesh;
        var p = (1f + MathF.Sqrt(5)) / 2;
        Vector3[] vertices = sides switch
        {
            4 => [new(1,1,1),new(1,-1,-1),new(-1,1,-1),new(-1,-1,1)],
            6 => (from x in new[]{-1f,1f} from y in new[]{-1f,1f} from z in new[]{-1f,1f} select new Vector3(x,y,z)).ToArray(),
            8 => [Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ],
            20 or 12 => (from a in new[]{-1f,1f} from b in new[]{-p,p} from axis in new[]{0,1,2} select axis == 0 ? new Vector3(0,a,b) : axis == 1 ? new Vector3(a,b,0) : new Vector3(b,0,a)).ToArray(),
            10 => Enumerable.Range(0,10).Select(i => { var angle = i * MathF.PI / 5; return new Vector3(MathF.Cos(angle),MathF.Sin(angle),i % 2 == 0 ? .55f : -.55f); }).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(sides))
        };
        mesh = Hull(vertices);
        if (sides is 12 or 10)
        {
            // Polar dual: icosahedron -> dodecahedron; pentagonal antiprism -> trapezohedron.
            var dual = mesh.Faces.Select(f =>
            {
                var normal = Vector3.Normalize(Vector3.Cross(vertices[f[1]] - vertices[f[0]], vertices[f[2]] - vertices[f[0]]));
                return normal / Vector3.Dot(normal, vertices[f[0]]);
            }).ToArray();
            mesh = Hull(dual);
        }
        var radius = mesh.Vertices.Max(v => v.Length());
        mesh = mesh with { Vertices = mesh.Vertices.Select(v => v / radius).ToArray() };
        Cache[sides] = mesh;
        return mesh;
    }
    private static DieMesh Hull(Vector3[] vertices)
    {
        var faces = new Dictionary<string,int[]>();
        for (var a=0; a<vertices.Length; a++) for (var b=a+1; b<vertices.Length; b++) for (var c=b+1; c<vertices.Length; c++)
        {
            var normal = Vector3.Cross(vertices[b]-vertices[a], vertices[c]-vertices[a]);
            if (normal.LengthSquared() < .00001f) continue;
            normal = Vector3.Normalize(normal);
            var distances = vertices.Select(v => Vector3.Dot(normal,v-vertices[a])).ToArray();
            if (distances.Any(d => d > .0001f) && distances.Any(d => d < -.0001f)) continue;
            var indices = Enumerable.Range(0,vertices.Length).Where(i => MathF.Abs(distances[i]) < .0001f).ToArray();
            var key = string.Join(',', indices); if (faces.ContainsKey(key)) continue;
            if (Vector3.Dot(normal,vertices[a]) < 0) normal = -normal;
            var center = indices.Aggregate(Vector3.Zero,(sum,i) => sum + vertices[i]) / indices.Length;
            var u = Vector3.Normalize(vertices[indices[0]]-center); var vAxis = Vector3.Cross(normal,u);
            faces[key] = indices.OrderBy(i => MathF.Atan2(Vector3.Dot(vertices[i]-center,vAxis),Vector3.Dot(vertices[i]-center,u))).ToArray();
        }
        return new(vertices, faces.Values.ToArray());
    }
    public Quaternion Landing(int face)
    {
        var f = Faces[face % Faces.Length];
        var n = Vector3.Normalize(Vector3.Cross(Vertices[f[1]]-Vertices[f[0]],Vertices[f[2]]-Vertices[f[0]]));
        var dot = Vector3.Dot(n,Vector3.UnitZ);
        return dot < -.9999f ? Quaternion.CreateFromAxisAngle(Vector3.UnitX,MathF.PI) : Quaternion.Normalize(new Quaternion(Vector3.Cross(n,Vector3.UnitZ),1 + dot));
    }
}
