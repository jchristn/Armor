using System;

namespace Armor.Publisher
{
    /// <summary>
    /// Code-signing invocations. The logic lives here; the credentials come from environment
    /// variables named by the manifest's signing block (which CI maps from GitHub secrets):
    /// the variable named by <c>vaultRef</c> holds a usable value (a cert file path on Windows,
    /// a signing-identity name on macOS, a key id for GPG), and <c>passwordRef</c>/<c>notarizeProfileRef</c>
    /// name the secrets for the passphrase / notarization profile. When a target is not configured,
    /// signing is skipped with a warning so unsigned artifacts still build.
    /// </summary>
    public static class Signing
    {
        /// <summary>Authenticode-signs a Windows file with signtool, if Windows signing is configured.</summary>
        public static void WindowsAuthenticode(string file, SigningTarget s)
        {
            if (!s.IsConfigured)
            {
                Console.WriteLine("[sign] windows: not configured — leaving UNSIGNED (SmartScreen will warn).");
                return;
            }
            string pfx = Env(s.VaultRef, "Windows cert file path");
            string pwd = Env(s.PasswordRef, "Windows cert password");
            ProcessRunner.Run("signtool", new[]
            {
                "sign", "/f", pfx, "/p", pwd,
                "/fd", "sha256", "/tr", "http://timestamp.digicert.com", "/td", "sha256",
                file
            });
        }

        /// <summary>Codesigns a macOS .app bundle with a hardened runtime, if macOS signing is configured.</summary>
        public static void MacCodesign(string appPath, SigningTarget s)
        {
            if (!s.IsConfigured)
            {
                Console.WriteLine("[sign] macOs: not configured — leaving UNSIGNED (Gatekeeper will block).");
                return;
            }
            string identity = Env(s.VaultRef, "macOS signing identity");
            ProcessRunner.Run("codesign", new[]
            {
                "--deep", "--force", "--options", "runtime", "--timestamp",
                "--sign", identity, appPath
            });
        }

        /// <summary>Submits a macOS artifact for notarization and staples it, if configured.</summary>
        public static void MacNotarizeStaple(string file, SigningTarget s)
        {
            if (!s.IsConfigured || !s.Notarize)
            {
                Console.WriteLine("[sign] macOs: notarization skipped (not configured).");
                return;
            }
            string profile = Env(s.NotarizeProfileRef, "notarytool keychain profile");
            ProcessRunner.Run("xcrun", new[] { "notarytool", "submit", file, "--keychain-profile", profile, "--wait" });
            ProcessRunner.Run("xcrun", new[] { "stapler", "staple", file });
        }

        /// <summary>Returns whether a given target is configured for signing.</summary>
        public static bool IsConfigured(SigningTarget s) => s.IsConfigured;

        private static string Env(string name, string what)
        {
            if (string.IsNullOrEmpty(name))
                throw new InvalidOperationException($"Signing is configured but no secret name was set for {what}.");
            string? value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value))
                throw new InvalidOperationException(
                    $"Signing is configured but environment variable '{name}' ({what}) is empty. " +
                    "CI must map the corresponding secret into this variable.");
            return value;
        }
    }
}
