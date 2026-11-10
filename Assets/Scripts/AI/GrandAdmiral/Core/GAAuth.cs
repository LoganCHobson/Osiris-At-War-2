using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Osiris.GrandAdmiral
{
    // Client half of the API's authentication (server/app/security.py).
    public static class GAAuth
    {
        public const int MaxBits = 26; // refuse absurd proof-of-work demands from a misbehaving server

        public static int LeadingZeroBits(byte[] digest)
        {
            int bits = 0;
            foreach (byte b in digest)
            {
                if (b == 0) { bits += 8; continue; }
                for (int mask = 0x80; mask != 0 && (b & mask) == 0; mask >>= 1) bits++;
                break;
            }
            return bits;
        }

        // Finds a nonce so that sha256(challenge ":" nonce) starts with `bits` zero bits. Run it off the
        // main thread: ~1M hashes at the default 20 bits, well under a second on any PC.
        public static string SolveChallenge(string challenge, int bits, CancellationToken cancel = default)
        {
            if (bits < 0 || bits > MaxBits) throw new ArgumentOutOfRangeException(nameof(bits));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] prefix = Encoding.UTF8.GetBytes(challenge + ":");
                for (long n = 0; ; n++)
                {
                    if ((n & 0xFFFF) == 0) cancel.ThrowIfCancellationRequested();
                    string nonce = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    byte[] tail = Encoding.ASCII.GetBytes(nonce);
                    byte[] message = new byte[prefix.Length + tail.Length];
                    Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
                    Buffer.BlockCopy(tail, 0, message, prefix.Length, tail.Length);
                    if (LeadingZeroBits(sha.ComputeHash(message)) >= bits) return nonce;
                }
            }
        }

        public static string Hex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string Sign(string tokenBase64, string method, string path, long unixTime, byte[] body)
        {
            string bodyHash;
            using (SHA256 sha = SHA256.Create()) bodyHash = Hex(sha.ComputeHash(body));
            string message = method + "\n" + path + "\n" + unixTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + bodyHash;
            using (HMACSHA256 hmac = new HMACSHA256(Convert.FromBase64String(tokenBase64)))
                return Hex(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
        }

        // Only HTTPS, except to this machine (local testing) or a Kubernetes service address (in-cluster
        // self-play, which never leaves the cluster network). Model bundles are signature-checked either way.
        public static bool IsAllowedServer(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
            if (uri.Scheme == Uri.UriSchemeHttps) return true;
            if (uri.Scheme != Uri.UriSchemeHttp) return false;
            string host = uri.Host.ToLowerInvariant();
            return uri.IsLoopback || host == "localhost" || host.EndsWith(".svc.cluster.local", StringComparison.Ordinal);
        }
    }
}
