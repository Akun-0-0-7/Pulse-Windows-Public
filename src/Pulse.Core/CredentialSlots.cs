namespace Pulse.Core;
public static class CredentialSlots
{
    public static IEnumerable<string> For(string accountId)=>[accountId,"console:"+accountId,"desktop:"+accountId];
    public static void Clear(ISecretStore secrets,string accountId){foreach(var slot in For(accountId))secrets.Set(slot,null);}
}
