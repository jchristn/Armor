namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Exceptions;
    using Armor.Core.Security;
    using Touchstone.Core;

    /// <summary>
    /// Verifies AES-256-GCM framing, PBKDF2 derivation, the keystore (passphrase and key-file
    /// wrapping), and the credential protector, with an emphasis on tamper and wrong-secret cases.
    /// </summary>
    public static class CryptoSuite
    {
        /// <summary>
        /// Build the cryptography test suite.
        /// </summary>
        /// <returns>The crypto suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Crypto",
                displayName: "Cryptography and Keystore",
                cases: new List<TestCaseDescriptor>
                {
                    Case("GcmRoundTrip", "AES-GCM encrypts and decrypts", _ =>
                    {
                        byte[] key = NewKey(1);
                        byte[] plaintext = Encoding.UTF8.GetBytes("the quick brown fox");
                        byte[] frame = AesGcmCipher.Encrypt(key, plaintext, null);
                        byte[] result = AesGcmCipher.Decrypt(key, frame, null);
                        Check.True(BytesEqual(plaintext, result), "decrypted matches plaintext");
                        return Task.CompletedTask;
                    }),

                    Case("GcmEmptyPlaintext", "AES-GCM handles empty plaintext", _ =>
                    {
                        byte[] key = NewKey(2);
                        byte[] frame = AesGcmCipher.Encrypt(key, Array.Empty<byte>(), null);
                        byte[] result = AesGcmCipher.Decrypt(key, frame, null);
                        Check.Equal(0, result.Length, "empty round-trips to empty");
                        return Task.CompletedTask;
                    }),

                    Case("GcmWrongKeyFails", "AES-GCM rejects the wrong key", _ =>
                    {
                        byte[] frame = AesGcmCipher.Encrypt(NewKey(3), Encoding.UTF8.GetBytes("secret"), null);
                        try
                        {
                            AesGcmCipher.Decrypt(NewKey(4), frame, null);
                            throw new InvalidOperationException("Expected ArmorCryptoException.");
                        }
                        catch (ArmorCryptoException)
                        {
                        }
                        return Task.CompletedTask;
                    }),

                    Case("GcmTamperFails", "AES-GCM rejects a tampered ciphertext", _ =>
                    {
                        byte[] key = NewKey(5);
                        byte[] frame = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes("integrity matters"), null);
                        frame[frame.Length - 1] ^= 0xFF;
                        try
                        {
                            AesGcmCipher.Decrypt(key, frame, null);
                            throw new InvalidOperationException("Expected ArmorCryptoException.");
                        }
                        catch (ArmorCryptoException)
                        {
                        }
                        return Task.CompletedTask;
                    }),

                    Case("GcmWrongAadFails", "AES-GCM rejects mismatched associated data", _ =>
                    {
                        byte[] key = NewKey(6);
                        byte[] frame = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes("bound"), Encoding.UTF8.GetBytes("aad-a"));
                        try
                        {
                            AesGcmCipher.Decrypt(key, frame, Encoding.UTF8.GetBytes("aad-b"));
                            throw new InvalidOperationException("Expected ArmorCryptoException.");
                        }
                        catch (ArmorCryptoException)
                        {
                        }
                        return Task.CompletedTask;
                    }),

                    Case("GcmTruncatedFails", "AES-GCM rejects a truncated frame", _ =>
                    {
                        try
                        {
                            AesGcmCipher.Decrypt(NewKey(7), new byte[] { 1, 2, 3 }, null);
                            throw new InvalidOperationException("Expected ArmorCryptoException.");
                        }
                        catch (ArmorCryptoException)
                        {
                        }
                        return Task.CompletedTask;
                    }),

                    Case("GcmBadKeyLength", "AES-GCM rejects a wrong-length key", _ =>
                    {
                        try
                        {
                            AesGcmCipher.Encrypt(new byte[16], Encoding.UTF8.GetBytes("x"), null);
                            throw new InvalidOperationException("Expected ArgumentException.");
                        }
                        catch (ArgumentException)
                        {
                        }
                        return Task.CompletedTask;
                    }),

                    Case("Pbkdf2Deterministic", "PBKDF2 is deterministic and salt-sensitive", _ =>
                    {
                        byte[] salt = new byte[16];
                        for (int i = 0; i < salt.Length; i++) salt[i] = (byte)i;
                        byte[] a = Pbkdf2KeyDeriver.DeriveKey("passphrase", salt, 50000);
                        byte[] b = Pbkdf2KeyDeriver.DeriveKey("passphrase", salt, 50000);
                        Check.True(BytesEqual(a, b), "same inputs give same key");
                        Check.Equal(32, a.Length, "derived key is 32 bytes");

                        byte[] other = new byte[16];
                        byte[] c = Pbkdf2KeyDeriver.DeriveKey("passphrase", other, 50000);
                        Check.False(BytesEqual(a, c), "different salt gives different key");
                        return Task.CompletedTask;
                    }),

                    Case("KeystorePassphraseRoundTrip", "Keystore wraps and unwraps by passphrase", _ =>
                    {
                        Keystore keystore = new Keystore();
                        ProvisionedKey provisioned = keystore.Provision("k1", "correct horse", null, 50000);
                        byte[] unlocked = keystore.UnlockWithPassphrase(provisioned.Key, "correct horse");
                        Check.True(BytesEqual(provisioned.DataKey, unlocked), "unlocked data key matches");
                        return Task.CompletedTask;
                    }),

                    Case("KeystoreKeyFileRoundTrip", "Keystore wraps and unwraps by key file", _ =>
                    {
                        Keystore keystore = new Keystore();
                        byte[] keyFile = KeyMaterial.GenerateKeyFileBytes();
                        ProvisionedKey provisioned = keystore.Provision("k2", null, keyFile, 50000);
                        byte[] unlocked = keystore.UnlockWithKeyFile(provisioned.Key, keyFile);
                        Check.True(BytesEqual(provisioned.DataKey, unlocked), "unlocked data key matches");
                        return Task.CompletedTask;
                    }),

                    Case("KeystoreBothMethodsAgree", "Both wrappings recover the same data key", _ =>
                    {
                        Keystore keystore = new Keystore();
                        byte[] keyFile = KeyMaterial.GenerateKeyFileBytes();
                        ProvisionedKey provisioned = keystore.Provision("k3", "pass phrase", keyFile, 50000);
                        byte[] viaPass = keystore.UnlockWithPassphrase(provisioned.Key, "pass phrase");
                        byte[] viaFile = keystore.UnlockWithKeyFile(provisioned.Key, keyFile);
                        Check.True(BytesEqual(provisioned.DataKey, viaPass), "passphrase recovers data key");
                        Check.True(BytesEqual(provisioned.DataKey, viaFile), "key file recovers data key");
                        return Task.CompletedTask;
                    }),

                    Case("KeystoreWrongPassphraseFails", "Keystore rejects the wrong passphrase", _ =>
                    {
                        Keystore keystore = new Keystore();
                        ProvisionedKey provisioned = keystore.Provision("k4", "right", null, 50000);
                        try
                        {
                            keystore.UnlockWithPassphrase(provisioned.Key, "wrong");
                            throw new InvalidOperationException("Expected ArmorCryptoException.");
                        }
                        catch (ArmorCryptoException)
                        {
                        }
                        return Task.CompletedTask;
                    }),

                    Case("KeystoreWrongKeyFileFails", "Keystore rejects the wrong key file", _ =>
                    {
                        Keystore keystore = new Keystore();
                        ProvisionedKey provisioned = keystore.Provision("k5", null, KeyMaterial.GenerateKeyFileBytes(), 50000);
                        try
                        {
                            keystore.UnlockWithKeyFile(provisioned.Key, KeyMaterial.GenerateKeyFileBytes());
                            throw new InvalidOperationException("Expected ArmorCryptoException.");
                        }
                        catch (ArmorCryptoException)
                        {
                        }
                        return Task.CompletedTask;
                    }),

                    Case("KeystoreRequiresAMethod", "Keystore requires a passphrase or key file", _ =>
                    {
                        Keystore keystore = new Keystore();
                        try
                        {
                            keystore.Provision("k6", null, null, 50000);
                            throw new InvalidOperationException("Expected ArgumentException.");
                        }
                        catch (ArgumentException)
                        {
                        }
                        return Task.CompletedTask;
                    }),

                    Case("KeystoreWrongMethodFails", "Unlocking by an unconfigured method fails", _ =>
                    {
                        Keystore keystore = new Keystore();
                        ProvisionedKey provisioned = keystore.Provision("k7", null, KeyMaterial.GenerateKeyFileBytes(), 50000);
                        try
                        {
                            keystore.UnlockWithPassphrase(provisioned.Key, "anything");
                            throw new InvalidOperationException("Expected ArmorCryptoException.");
                        }
                        catch (ArmorCryptoException)
                        {
                        }
                        return Task.CompletedTask;
                    }),

                    Case("CredentialProtectorRoundTrip", "Credential protector persists a reusable key", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            string keyPath = ws.Combine("dp.key");
                            CredentialProtector first = new CredentialProtector(keyPath);
                            string secret = "s3-secret-'value'";
                            string protectedValue = await first.ProtectAsync(secret, ct).ConfigureAwait(false);
                            Check.False(protectedValue.Contains(secret, StringComparison.Ordinal), "protected value does not contain plaintext");

                            CredentialProtector second = new CredentialProtector(keyPath);
                            string recovered = await second.UnprotectAsync(protectedValue, ct).ConfigureAwait(false);
                            Check.Equal(secret, recovered, "second instance recovers the secret via the shared key file");
                        }
                    }),

                    Case("CredentialProtectorTamperFails", "Credential protector rejects tampered values", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            CredentialProtector protector = new CredentialProtector(ws.Combine("dp.key"));
                            string protectedValue = await protector.ProtectAsync("secret", ct).ConfigureAwait(false);
                            byte[] raw = Convert.FromBase64String(protectedValue);
                            raw[raw.Length - 1] ^= 0xFF;
                            string tampered = Convert.ToBase64String(raw);
                            await Check.ThrowsAsync<ArmorCryptoException>(
                                () => protector.UnprotectAsync(tampered, ct),
                                "tampered credential should fail").ConfigureAwait(false);
                        }
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Crypto",
                caseId: caseId,
                displayName: displayName,
                executeAsync: body);
        }

        private static byte[] NewKey(int seed)
        {
            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++)
                key[i] = (byte)((i * 7 + seed) & 0xFF);
            return key;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}
