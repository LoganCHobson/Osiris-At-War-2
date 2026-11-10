using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Osiris.GrandAdmiral
{
    // A signed model bundle: "OGAB" | u16 format | u16 key id | u32 model length | model | u16 sig length | sig.
    // RSA-3072 PKCS#1 v1.5 over SHA-256(context + model). The game only runs models signed by a key
    // in GATrustedKeys, which only the offline trainer holds: not the API server, not the CDN.
    public static class GABundle
    {
        public const int MaxModelBytes = 1 << 20;

        // Returns the verified model bytes, or throws.
        public static byte[] Verify(byte[] bundle, IReadOnlyDictionary<ushort, RSAParameters> trustedKeys)
        {
            if (bundle == null || bundle.Length < 12) throw new GAFormatException("truncated bundle");
            ushort keyId;
            byte[] model, signature;
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bundle, false)))
            {
                if (reader.ReadByte() != 'O' || reader.ReadByte() != 'G' || reader.ReadByte() != 'A' || reader.ReadByte() != 'B')
                    throw new GAFormatException("not a bundle");
                if (reader.ReadUInt16() != GASpec.BundleFormat) throw new GAFormatException("unsupported bundle format");
                keyId = reader.ReadUInt16();
                uint modelLength = reader.ReadUInt32();
                if (modelLength > MaxModelBytes || 12L + modelLength + 2 > bundle.Length) throw new GAFormatException("bad model length");
                model = reader.ReadBytes((int)modelLength);
                ushort sigLength = reader.ReadUInt16();
                if (12L + modelLength + 2 + sigLength != bundle.Length) throw new GAFormatException("bad signature length");
                signature = reader.ReadBytes(sigLength);
            }

            if (trustedKeys == null || !trustedKeys.TryGetValue(keyId, out RSAParameters key))
                throw new GAFormatException($"untrusted key {keyId}");

            byte[] signed = new byte[GASpec.SignatureContext.Length + model.Length];
            Buffer.BlockCopy(GASpec.SignatureContext, 0, signed, 0, GASpec.SignatureContext.Length);
            Buffer.BlockCopy(model, 0, signed, GASpec.SignatureContext.Length, model.Length);

            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(key);
                if (rsa.KeySize < 3072) throw new GAFormatException("trusted key too weak");
                if (!rsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    throw new GAFormatException("bad signature");
            }
            return model;
        }

        // Verify, parse, and refuse anything older than what we already run (no downgrades).
        public static GAModel Load(byte[] bundle, IReadOnlyDictionary<ushort, RSAParameters> trustedKeys, int minimumVersion = 0)
        {
            GAModel model = GAModel.FromBytes(Verify(bundle, trustedKeys));
            if (model.Version < minimumVersion) throw new GAFormatException($"model v{model.Version} is older than v{minimumVersion}");
            return model;
        }

        public static RSAParameters PublicKey(string modulusBase64, string exponentBase64)
        {
            return new RSAParameters { Modulus = Convert.FromBase64String(modulusBase64), Exponent = Convert.FromBase64String(exponentBase64) };
        }
    }
}
