using System.Security.Cryptography;
using System.Text;

namespace LanguageData.Services;

/// <summary>
/// AES-256-CBC Verschlüsselung mit geräte-spezifischem Schlüssel.
/// </summary>
public static class CryptoHelper
{
    private static string? _basePath;
    private const string PRIMARY_FILE = "langdata.dat";
    private const string BACKUP_FILE = "langdata.bak";

    public static void Initialize(string basePath)
    {
        _basePath = basePath;
        try
        {
            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);
        }
        catch { }
    }

    /// <summary>
    /// Verschlüsselt Daten und speichert sie (Primär + Backup).
    /// </summary>
    public static void SaveEncrypted(string json)
    {
        if (_basePath == null) return;

        try
        {
            byte[] encrypted = Encrypt(json);
            string primary = Path.Combine(_basePath, PRIMARY_FILE);
            string backup = Path.Combine(_basePath, BACKUP_FILE);

            // Primärdatei schreiben
            WriteWithRetry(primary, encrypted);

            // Backup schreiben
            WriteWithRetry(backup, encrypted);
        }
        catch { }
    }

    /// <summary>
    /// Lädt und entschlüsselt Daten (Primär, Fallback auf Backup).
    /// </summary>
    public static string? LoadEncrypted()
    {
        if (_basePath == null) return null;

        string primary = Path.Combine(_basePath, PRIMARY_FILE);
        string backup = Path.Combine(_basePath, BACKUP_FILE);

        // Primärdatei versuchen
        var result = TryDecryptFile(primary);
        if (result != null) return result;

        // Backup versuchen
        return TryDecryptFile(backup);
    }

    private static string? TryDecryptFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            byte[] data = ReadWithRetry(path);
            if (data == null || data.Length == 0) return null;
            return Decrypt(data);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = DeriveKey();
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // IV + Encrypted Data zusammenfügen
        byte[] result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
        return result;
    }

    private static string Decrypt(byte[] data)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = DeriveKey();

        // IV aus den ersten 16 Bytes extrahieren
        byte[] iv = new byte[16];
        Buffer.BlockCopy(data, 0, iv, 0, 16);
        aes.IV = iv;

        byte[] encrypted = new byte[data.Length - 16];
        Buffer.BlockCopy(data, 16, encrypted, 0, encrypted.Length);

        using var decryptor = aes.CreateDecryptor();
        byte[] decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        return Encoding.UTF8.GetString(decrypted);
    }

    private static byte[] DeriveKey()
    {
        // Geräte-spezifischer Schlüssel
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
                if (i < retries - 1) Thread.Sleep(100);
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
                if (i < retries - 1) Thread.Sleep(100);
            }
        }
        return Array.Empty<byte>();
    }
}
