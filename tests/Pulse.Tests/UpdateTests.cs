using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Pulse.Core;
namespace Pulse.Tests;

public static class UpdateTests
{
    private static byte[] Zip(params (string Name,string Value,int Attributes)[] entries)
    {
        using var output=new MemoryStream();using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))foreach(var value in entries){var entry=zip.CreateEntry(value.Name);entry.ExternalAttributes=value.Attributes;using var stream=entry.Open();stream.Write(Encoding.UTF8.GetBytes(value.Value));}return output.ToArray();
    }
    private static UpdateManifest Manifest(byte[] bytes)=>new(new Version(0,2,0),new Uri("https://updates.invalid/pulse.zip"),Convert.ToHexString(SHA256.HashData(bytes)));
    private static string Temporary(){var root=Path.Combine(Path.GetTempPath(),"pulse-update-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);return root;}
    private static async Task Rejected(byte[] bytes,UpdateLimits? limits=null)
    {
        var root=Temporary();try{try{await UpdatePackage.PrepareArchiveAsync(new MemoryStream(bytes),Manifest(bytes),root,limits:limits);throw new Exception("Unsafe archive accepted");}catch(InvalidDataException){}Assert.True(!Directory.Exists(Path.Combine(root,"updates"))||!Directory.EnumerateFileSystemEntries(Path.Combine(root,"updates")).Any(),"Rejected update left staged files");}finally{Directory.Delete(root,true);}
    }
    [Check] public static void UpdateManifestRequiresHttpsVersionAndExactSha256()
    {
        var hash=new string('A',64);var value=UpdatePackage.ParseManifest("{\"version\":\"0.2.0\",\"url\":\"https://updates.invalid/v2.zip\",\"sha256\":\""+hash+"\"}");Assert.Equal("0.2.0",value.Version.ToString());
        foreach(var json in new[]{"{}","{\"version\":\"latest\"}","{\"version\":\"0.2.0\",\"url\":\"http://updates.invalid/a.zip\",\"sha256\":\""+hash+"\"}","{\"version\":\"0.2.0\",\"url\":\"https://updates.invalid/a.zip\",\"sha256\":\"bad\"}"})Assert.Throws<InvalidDataException>(()=>UpdatePackage.ParseManifest(json));
    }
    [Check] public static async Task UpdateArchivePreparesOnlyInsideRandomUpdateDirectory()
    {
        var bytes=Zip(("release/Pulse.exe","new desktop",0),("release/Pulse-cli.exe","new cli",0),("release/Docs/readme.md","help",0));var root=Temporary();try{var prepared=await UpdatePackage.PrepareArchiveAsync(new MemoryStream(bytes),Manifest(bytes),root);Assert.Equal(3,prepared.Files.Count);Assert.True(prepared.PreparationDirectory.StartsWith(Path.Combine(root,"updates")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));Assert.Equal("new desktop",File.ReadAllText(Path.Combine(prepared.PackageRoot,"Pulse.exe")));Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),prepared.Sha256);}finally{Directory.Delete(root,true);}
    }
    [Check] public static async Task UpdateArchiveRejectsWrongChecksumBeforeExtraction()
    {
        var bytes=Zip(("Pulse.exe","desktop",0),("Pulse-cli.exe","cli",0));var root=Temporary();try{try{await UpdatePackage.PrepareArchiveAsync(new MemoryStream(bytes),Manifest(bytes) with{Sha256=new string('0',64)},root);throw new Exception("Checksum mismatch accepted");}catch(InvalidDataException){}Assert.True(!Directory.EnumerateFiles(root,"Pulse.exe",SearchOption.AllDirectories).Any());}finally{Directory.Delete(root,true);}
    }
    [Check] public static async Task UpdateArchiveRejectsTraversalLinksAndCaseCollisions()
    {
        foreach(var bad in new[]{"../outside.txt","/absolute.txt","C:/outside.txt","folder/../outside.txt","folder\\outside.txt","con.txt","trailing. /file","file:stream"})await Rejected(Zip(("Pulse.exe","desktop",0),("Pulse-cli.exe","cli",0),(bad,"bad",0)));
        await Rejected(Zip(("Pulse.exe","desktop",0),("Pulse-cli.exe","cli",0),("link","elsewhere",unchecked((int)0xA1FF0000))));
        await Rejected(Zip(("Pulse.exe","desktop",0),("Pulse-cli.exe","cli",0),("PULSE.EXE","duplicate",0)));
    }
    [Check] public static async Task UpdateArchiveEnforcesCountSizeAndExpansionLimits()
    {
        var bytes=Zip(("Pulse.exe","desktop",0),("Pulse-cli.exe","cli",0),("Docs/large.txt",new string('x',10000),0));await Rejected(bytes,new(MaxEntries:2));await Rejected(bytes,new(MaxEntryBytes:10));await Rejected(bytes,new(MaxExtractedBytes:20));await Rejected(bytes,new(MaxDownloadBytes:20));await Rejected(bytes,new(MaxCompressionRatio:2));
    }
    [Check] public static async Task UpdateArchiveRequiresOneCompleteReleaseRoot()
    {
        await Rejected(Zip(("Pulse.exe","desktop",0)));await Rejected(Zip(("a/Pulse.exe","desktop",0),("a/Pulse-cli.exe","cli",0),("b/Pulse.exe","desktop",0),("b/Pulse-cli.exe","cli",0)));
    }
    [Check] public static async Task UpdateReadManifestAndDownloadUseBoundedHttpBodies()
    {
        var bytes=Zip(("Pulse.exe","desktop",0),("Pulse-cli.exe","cli",0));var root=Temporary();try
        {
            using var http=new HttpClient(new ReplyHandler(request=>new HttpResponseMessage(System.Net.HttpStatusCode.OK){RequestMessage=request,Content=request.RequestUri!.AbsolutePath.EndsWith("json")?new StringContent("{\"version\":\"0.2.0\",\"url\":\"https://updates.invalid/pulse.zip\",\"sha256\":\""+Manifest(bytes).Sha256+"\"}"):new ByteArrayContent(bytes)}));
            var manifest=await UpdatePackage.ReadManifestAsync(http,new Uri("https://updates.invalid/release.json"));var prepared=await UpdatePackage.PrepareAsync(http,manifest,root);Assert.Equal(2,prepared.Files.Count);
            using var oversized=new HttpClient(new ReplyHandler(request=>new HttpResponseMessage(System.Net.HttpStatusCode.OK){RequestMessage=request,Content=new StringContent(new string('x',65537))}));
            try{await UpdatePackage.ReadManifestAsync(oversized,new Uri("https://updates.invalid/release.json"));throw new Exception("Oversized manifest accepted");}catch(InvalidDataException){}
            using var downgrade=new HttpClient(new ReplyHandler(_=>new HttpResponseMessage(System.Net.HttpStatusCode.OK){RequestMessage=new(HttpMethod.Get,"http://updates.invalid/release.json"),Content=new StringContent("{}")}));
            try{await UpdatePackage.ReadManifestAsync(downgrade,new Uri("https://updates.invalid/release.json"));throw new Exception("Non-HTTPS final response accepted");}catch(InvalidDataException){}
        }finally{Directory.Delete(root,true);}
    }
    [Check] public static async Task UpdateCancellationRemovesItsStagingDirectory()
    {
        var bytes=Zip(("Pulse.exe","desktop",0),("Pulse-cli.exe","cli",0));var root=Temporary();try{using var cancellation=new CancellationTokenSource();cancellation.Cancel();try{await UpdatePackage.PrepareArchiveAsync(new MemoryStream(bytes),Manifest(bytes),root,cancellation.Token);throw new Exception("Cancelled update prepared");}catch(OperationCanceledException){}Assert.True(!Directory.EnumerateFileSystemEntries(Path.Combine(root,"updates")).Any());}finally{Directory.Delete(root,true);}
    }
    [Check] public static async Task UpdateInstallerRejectsModifiedPreparedFilesWithoutTouchingTarget()
    {
        var bytes=Zip(("Pulse.exe","new desktop",0),("Pulse-cli.exe","new cli",0));var root=Temporary();try{var prepared=await UpdatePackage.PrepareArchiveAsync(new MemoryStream(bytes),Manifest(bytes),root);var target=Path.Combine(root,"target");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"Pulse.exe"),"old desktop");File.WriteAllText(Path.Combine(target,"Pulse-cli.exe"),"old cli");File.WriteAllText(Path.Combine(prepared.PackageRoot,"Pulse.exe"),"bad desktop");Assert.Throws<InvalidDataException>(()=>UpdatePackage.WriteInstaller(prepared,target,0,false));Assert.Equal("old desktop",File.ReadAllText(Path.Combine(target,"Pulse.exe")));}finally{Directory.Delete(root,true);}
    }
    private sealed class ReplyHandler(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(reply(request));}
    [Check] public static async Task UpdateInstallerCopiesOnlyPackageFilesAndRollsBackOnCopyFailure()
    {
        if(!OperatingSystem.IsWindows())return;var root=Temporary();try{
            var target=Path.Combine(root,"target");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"Pulse.exe"),"old desktop");File.WriteAllText(Path.Combine(target,"Pulse-cli.exe"),"old cli");File.WriteAllText(Path.Combine(target,"user-file.txt"),"keep");
            async Task<PreparedUpdate> Prepare(byte[] bytes)=>await UpdatePackage.PrepareArchiveAsync(new MemoryStream(bytes),Manifest(bytes),Path.Combine(root,"data"));
            var good=await Prepare(Zip(("Pulse.exe","new desktop",0),("Pulse-cli.exe","new cli",0),("Docs/readme.md","help",0)));var installer=UpdatePackage.WriteInstaller(good,target,0,false);var result=await ProcessRunner.RunAsync("powershell.exe",["-NoLogo","-NoProfile","-ExecutionPolicy","Bypass","-File",installer.ScriptPath,"-PlanFile",installer.PlanPath],TimeSpan.FromSeconds(30));Assert.True(result.ExitCode==0,result.Error+" | "+(File.Exists(installer.LogPath)?File.ReadAllText(installer.LogPath):"no install log"));Assert.Equal("new desktop",File.ReadAllText(Path.Combine(target,"Pulse.exe")));Assert.Equal("keep",File.ReadAllText(Path.Combine(target,"user-file.txt")));Assert.True(File.ReadAllText(installer.LogPath).Contains("SUCCESS"));
            File.WriteAllText(Path.Combine(target,"zz-blocker"),"existing file");var bad=await Prepare(Zip(("Pulse.exe","changed desktop",0),("Pulse-cli.exe","changed cli",0),("new-file.txt","remove on rollback",0),("zz-blocker/child.txt","cannot create",0)));var rollback=UpdatePackage.WriteInstaller(bad,target,0,false);var failed=await ProcessRunner.RunAsync("powershell.exe",["-NoLogo","-NoProfile","-ExecutionPolicy","Bypass","-File",rollback.ScriptPath,"-PlanFile",rollback.PlanPath],TimeSpan.FromSeconds(30));Assert.True(failed.ExitCode!=0);Assert.Equal("new desktop",File.ReadAllText(Path.Combine(target,"Pulse.exe")));Assert.Equal("new cli",File.ReadAllText(Path.Combine(target,"Pulse-cli.exe")));Assert.True(!File.Exists(Path.Combine(target,"new-file.txt")));Assert.Equal("keep",File.ReadAllText(Path.Combine(target,"user-file.txt")));Assert.True(File.ReadAllText(rollback.LogPath).Contains("ROLLBACK"));
        }finally{Directory.Delete(root,true);}
    }
}

