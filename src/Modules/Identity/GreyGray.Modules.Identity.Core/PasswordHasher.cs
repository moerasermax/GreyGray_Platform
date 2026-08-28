using System.Security.Cryptography;

namespace GreyGray.Modules.Identity.Core;

internal sealed class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Algorithm = "pbkdf2-sha256-v1";
    private readonly string _dummyHash;

    public PasswordHasher() => _dummyHash = Hash("greygray-dummy-password");

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return string.Join(
            '$',
            Algorithm,
            Iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string? encodedHash)
    {
        var value = string.IsNullOrWhiteSpace(encodedHash) ? _dummyHash : encodedHash;
        var parts = value.Split('$');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], Algorithm, StringComparison.Ordinal) ||
            !int.TryParse(parts[1], out var iterations) ||
            iterations < 100_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
