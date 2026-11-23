using System.Security.Cryptography;
using System.Text;

namespace ChatMixr.Models;

/// <summary>
/// The security ID Windows gives a package's sandbox (its AppContainer), worked out from the
/// package family name. Used to let only ChatMixr's own Game Bar widget open the widget pipe.
/// <para>
/// Windows' own <c>DeriveAppContainerSidFromAppContainerName</c> is asked first; called from inside
/// an installed package it was seen to report success and return no SID (Windows 11, 2026-11-22), so
/// this is the fallback. Windows builds the SID as <c>S-1-15-2-</c> followed by the first 28 bytes of
/// the SHA-256 of the lower-cased name (UTF-16LE), read as seven little-endian 32-bit numbers. That
/// rule isn't documented, so a test pins it to a SID Windows reported for ChatMixr's dev package.
/// </para>
/// </summary>
public static class AppContainerSid
{
    /// <summary>The SID, as text, for a package family name (or any AppContainer name).</summary>
    public static string FromName(string appContainerName)
    {
        byte[] hash = SHA256.HashData(Encoding.Unicode.GetBytes(appContainerName.ToLowerInvariant()));

        var sid = new StringBuilder("S-1-15-2");
        for (int i = 0; i < 7; i++)
        {
            sid.Append('-').Append(BitConverter.ToUInt32(hash, i * 4));
        }

        return sid.ToString();
    }
}
