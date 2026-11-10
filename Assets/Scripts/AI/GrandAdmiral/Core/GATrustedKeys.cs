using System.Collections.Generic;
using System.Security.Cryptography;

namespace Osiris.GrandAdmiral
{
    // Public keys whose signatures the game accepts on Grand Admiral models. Generate with
    //     osiris-ml keygen --out ga-signing-1.pem --key-id 1
    // and paste the line it prints below. To rotate: add the new key in one game update, switch the
    // trainer to it, then drop the old key in a later update. A key can be removed here to revoke it.
    public static class GATrustedKeys
    {
        private static readonly Dictionary<ushort, (string modulus, string exponent)> Keys =
            new Dictionary<ushort, (string, string)>
            {
                // { 1, ("<base64 modulus>", "AQAB") },
            };

        private static Dictionary<ushort, RSAParameters> parsed;

        public static IReadOnlyDictionary<ushort, RSAParameters> All
        {
            get
            {
                if (parsed == null)
                {
                    parsed = new Dictionary<ushort, RSAParameters>();
                    foreach (KeyValuePair<ushort, (string modulus, string exponent)> key in Keys)
                    {
                        parsed[key.Key] = GABundle.PublicKey(key.Value.modulus, key.Value.exponent);
                    }
                }
                return parsed;
            }
        }

        public static bool Configured => Keys.Count > 0;
    }
}
