using System.Numerics;

namespace DiceMaster;

public static class FaceTextureCoordinates
{
    // Object-space planar mapping: the texture rotates with the face, never with
    // the screen. Cache immutable mesh face coordinates to avoid work per frame.
    private static readonly Dictionary<int[],Vector2[]> Cache = new();
    public static Vector2[] For(DieMesh mesh,int[] face)
    {
        if (Cache.TryGetValue(face,out var result)) return result;
        var center=face.Aggregate(Vector3.Zero,(sum,i)=>sum+mesh.Vertices[i])/face.Length;
        var tangent=Vector3.Normalize(mesh.Vertices[face[1]]-mesh.Vertices[face[0]]);
        var normal=Vector3.Normalize(Vector3.Cross(tangent,mesh.Vertices[face[2]]-mesh.Vertices[face[0]]));
        var bitangent=Vector3.Cross(normal,tangent);
        var radius=face.Max(i=>(mesh.Vertices[i]-center).Length());
        result=face.Select(i=>new Vector2(.5f)+new Vector2(Vector3.Dot(mesh.Vertices[i]-center,tangent),Vector3.Dot(mesh.Vertices[i]-center,bitangent))*(.46f/radius)).ToArray();
        Cache[face]=result; return result;
    }
}
