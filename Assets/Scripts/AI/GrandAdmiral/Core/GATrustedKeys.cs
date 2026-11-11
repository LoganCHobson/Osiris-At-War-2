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
                { 1, ("q/Ro2LpaTq9w6Y4tjrK+8J6uNDgEVQp8CfQH1KOUPMglMyUkaPVbdIv3ujT4g1ecqMTTl8igkzuUcFrdApxEfy6JdkMu1SYXQ9x2Zd571zcjKMeiZuQDMhf+r2BwyDiaSoe0x/4muu2JY7yBP3T4SUmZeUnB525C/ggrJW7y6JFkAEgqhyX7oTHISCX/QPNFxIXdutsrwgLoh0RsXpbmS9LZJYFytqo4dwJIDzDtcmywmj3XZSWRyziuWKu+kWIXpAND31gF3B19+SfNMqykc3y6zoQDNFh+OBTtHFs/j+UACaHmrVDPrP8cKyumExeAE8uGNroMTWrwcV5QwvuSrFxMIheesTre/6Qva9+X4KcVRijYvZwPqKLJ6rcozCwm7bEZBPdPm3K7DkCkZdRWi2pUDrHATUKYN3PbJDWDfyT4IHEb0t93LnTmx/VSU4tQVFB+yU5xSX4dpfyznhIrbp6/x6IxbIC6sJqKGqc3Iqa/ijz8vZy6pYgReWTIBc0D", "AQAB") },
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
