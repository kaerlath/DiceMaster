using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiceMaster;

public sealed class CredentialStore(string directory)
{
    private readonly string path=Path.Combine(directory,"server-auth.dpapi");
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("DiceMaster server authentication v1");
    private sealed record Saved(string Origin,string Credential);
    public bool Exists => File.Exists(path);
    public void Save(Uri origin,string credential)
    {
        Directory.CreateDirectory(directory);
        var plain=JsonSerializer.SerializeToUtf8Bytes(new Saved(origin.GetLeftPart(UriPartial.Authority),credential));
        try
        {
            var encrypted=ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path+".new",encrypted);
            File.Move(path+".new",path,true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public string? Read(Uri origin)
    {
        if (!Exists) return null;
        var encrypted=File.ReadAllBytes(path);
        var plain=ProtectedData.Unprotect(encrypted,Entropy,DataProtectionScope.CurrentUser);
        try
        {
            var saved=JsonSerializer.Deserialize<Saved>(plain);
            // Never send a saved secret to a different relay configured later.
            return saved?.Origin==origin.GetLeftPart(UriPartial.Authority) ? saved.Credential : null;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Forget() { if (Exists) File.Delete(path); }
}
