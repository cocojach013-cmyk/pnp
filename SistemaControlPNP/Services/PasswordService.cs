using System;
using System.Security.Cryptography;
using ControlPNP.Service;

namespace ControlPNP.Services
{
    public sealed class PasswordService : IPasswordService
    {
        private const int Iterations = 100_000;
        private const int SaltSize = 16;
        private const int KeySize = 32;

        public string Hash(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password vacío.", nameof(password));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
            byte[] key = pbkdf2.GetBytes(KeySize);
            return $"PBKDF2|{Iterations}|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(key)}";
        }

        public bool Verify(string password, string hash)
        {
            if (string.IsNullOrEmpty(hash)) return false;

            var parts = hash.Split('|');
            if (parts.Length != 4 || parts[0] != "PBKDF2") return false;

            int iterations = int.Parse(parts[1]);
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expectedKey = Convert.FromBase64String(parts[3]);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            byte[] actualKey = pbkdf2.GetBytes(expectedKey.Length);
            return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
        }
    }
}
