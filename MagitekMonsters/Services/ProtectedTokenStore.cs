using System;
using System.Security.Cryptography;
using System.Text;

namespace MagitekMonsters.Services;

public sealed class ProtectedTokenStore(Configuration configuration)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MagitekMonsters.Dalamud.Session.v1");

    public string? Read()
    {
        if (string.IsNullOrWhiteSpace(configuration.ProtectedAccessToken))
            return null;

        try
        {
            var encrypted = Convert.FromBase64String(configuration.ProtectedAccessToken);
            var plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "The saved Magitek Monsters session could not be decrypted; clearing it.");
            Clear();
            return null;
        }
    }

    public void Write(string token)
    {
        var plaintext = Encoding.UTF8.GetBytes(token);
        var encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        configuration.ProtectedAccessToken = Convert.ToBase64String(encrypted);
        configuration.Save();
    }

    public void Clear()
    {
        configuration.ProtectedAccessToken = string.Empty;
        configuration.Save();
    }
}
