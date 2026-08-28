using System.Security.Cryptography;
using System.Text;
using GreyGray.Modules.Identity.Core;

namespace GreyGray.Modules.Identity.Infra;

internal sealed class IdentityDataProtector
    : IIdentityDataProtector
{
    private const string Version = "aes256gcm-v1";
    private readonly byte[] _key;

    public IdentityDataProtector(string base64Key)
    {
        try
        {
            _key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Identity:DataProtectionKey 必須是 Base64 編碼的 32-byte 金鑰。",
                exception);
        }

        if (_key.Length != 32)
        {
            throw new InvalidOperationException(
                "Identity:DataProtectionKey 解碼後必須正好是 32 bytes（AES-256）。");
        }
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        return string.Join(
            ':',
            Version,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(tag));
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);
        var parts = protectedValue.Split(':');
        if (parts.Length != 4 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
        {
            throw new CryptographicException("不支援的 Identity 個資密文格式。");
        }

        try
        {
            var nonce = Convert.FromBase64String(parts[1]);
            var ciphertext = Convert.FromBase64String(parts[2]);
            var tag = Convert.FromBase64String(parts[3]);
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(_key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("Identity 個資密文格式無效。", exception);
        }
    }

    public string CreateLookup(string purpose, string normalizedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedValue);
        var data = Encoding.UTF8.GetBytes($"{purpose}\0{normalizedValue}");
        var digest = HMACSHA256.HashData(_key, data);
        return Convert.ToHexStringLower(digest);
    }
}
