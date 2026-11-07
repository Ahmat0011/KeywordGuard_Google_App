using System.Security.Cryptography;
using System.Text;

namespace LanguageData.Services;

/// <summary>
/// AES-256-CBC Verschlüsselung mit stabilem Schlüssel und automatischer Migration.
/// </summary>
public static class CryptoHelper
{
    private static string? _basePath;
    private const string PRIMARY_FILE = "langdata.dat";
    private const string BACKUP_FILE = "langdata.bak";
    private const string KEY_SEED_FIXED = "LD_Stable_Android_Key_v3_98f7a6d5";

    public static void Initialize(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath)) return;
        _basePath = basePath;
        try
        {
            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);
        }
        catch { }
    }

    public static void SaveEncrypted(string json)
    {
        EnsureInitialized();
        if (_basePath == null) return;

        try
        {
            byte[] encrypted = Encrypt(json, DeriveStableKey());
            string primary = Path.Combine(_basePath, PRIMARY_FILE);
            string backup = Path.Combine(_basePath, BACKUP_FILE);

            WriteWithRetry(primary, encrypted);
            WriteWithRetry(backup, encrypted);
        }
        catch { }
    }

    public static string? LoadEncrypted()
    {
        EnsureInitialized();
        if (_basePath == null) return null;

        string primary = Path.Combine(_basePath, PRIMARY_FILE);
        string backup = Path.Combine(_basePath, BACKUP_FILE);

        var result = TryDecryptFile(primary);
        if (result != null) return result;

        return TryDecryptFile(backup);
    }

    private static void EnsureInitialized()
    {
        if (_basePath != null) return;
        try
        {
#if ANDROID
            var filesDir = global::Android.App.Application.Context?.FilesDir;
            if (filesDir != null)
            {
                Initialize(filesDir.AbsolutePath);
                return;
            }
#endif
            Initialize(Environment.GetFolderPath(Environment.SpecialFolder.Personal));
        }
        catch { }
    }

    private static string? TryDecryptFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            byte[] data = ReadWithRetry(path);
            if (data == null || data.Length <= 16) return null;

            // 1. Mit neuem stabilen Schlüssel versuchen
            try
            {
                return Decrypt(data, DeriveStableKey());
            }
            catch
            {
                // 2. Fallback auf alten Schlüssel (Migration bestehender Daten)
                string migrated = Decrypt(data, DeriveLegacyKey());
                if (!string.IsNullOrEmpty(migrated))
                {
                    SaveEncrypted(migrated);
                }
                return migrated;
            }
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Encrypt(string plaintext, byte[] key)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        byte[] result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
        return result;
    }

    private static string Decrypt(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;

        byte[] iv = new byte[16];
        Buffer.BlockCopy(data, 0, iv, 0, 16);
        aes.IV = iv;

        byte[] encrypted = new byte[data.Length - 16];
        Buffer.BlockCopy(data, 16, encrypted, 0, encrypted.Length);

        using var decryptor = aes.CreateDecryptor();
        byte[] decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        return Encoding.UTF8.GetString(decrypted);
    }

    private static byte[] DeriveStableKey()
    {
        string pkg = "com.android.languagedata.service";
#if ANDROID
        try { pkg = global::Android.App.Application.Context?.PackageName ?? pkg; } catch { }
#endif
        return SHA256.HashData(Encoding.UTF8.GetBytes($"{KEY_SEED_FIXED}_{pkg}"));
    }

    private static byte[] DeriveLegacyKey()
    {
        string seed = $"LD_{Environment.MachineName}_{Environment.ProcessorCount}_{Environment.OSVersion}_v2";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }

    private static void WriteWithRetry(string path, byte[] data, int retries = 3)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                File.WriteAllBytes(path, data);
                return;
            }
            catch
            {
                if (i < retries - 1) Thread.Sleep(50);
            }
        }
    }

    private static byte[] ReadWithRetry(string path, int retries = 3)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch
            {
                if (i < retries - 1) Thread.Sleep(50);
            }
        }
        return Array.Empty<byte>();
    }
}