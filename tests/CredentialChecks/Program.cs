using DiceMaster;

var directory=Path.GetFullPath(args[0]);
var store=new CredentialStore(directory);
var uri=new Uri("https://test.invalid/");
var secret=Guid.NewGuid()+"."+new string('a',64);
if (store.Read(uri)!=null) throw new Exception("Unexpected preexisting test credential");
store.Save(uri,secret);
if (!store.Exists) throw new Exception("Credential not persisted");
var fresh=new CredentialStore(directory);
if (fresh.Read(uri)!=secret) throw new Exception("Reload lost the saved credential");
if (fresh.Read(new Uri("https://other.invalid/"))!=null) throw new Exception("Cross-relay secret exposure");
if (System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory,"server-auth.dpapi"))).Contains(secret)) throw new Exception("Plaintext secret written");
fresh.Save(uri,secret+"updated");
if (fresh.Read(uri)!=secret+"updated") throw new Exception("Replacement failed");
fresh.Forget();
if (store.Exists || fresh.Read(uri)!=null) throw new Exception("Forget failed");
Console.WriteLine("Credential checks passed: encrypted storage, reload, origin binding, replacement and forget.");
