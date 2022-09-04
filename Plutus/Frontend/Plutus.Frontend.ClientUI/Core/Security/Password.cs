using System.Security.Cryptography;

namespace Plutus.Frontend.ClientUI.Core.Security
{
    /// <summary>
    /// This deals with all password encryption, decryption and comparison
    /// </summary>
    internal static class Password
    {
        private const int SaltByteSize = 64;
        private const int HashByteSize = 64;
        private const int HasingIterationsCount = 101010;

        /// <summary>
        /// Generates Salt for password hashing
        /// </summary>
        /// <param name="saltByteSize">This is constant</param>
        /// <returns>Byte[] salt</returns>
        internal static byte[] GenerateSalt(int saltByteSize = SaltByteSize)
        {
            using (var saltGenerator = RandomNumberGenerator.Create())
            {
                byte[] salt = new byte[saltByteSize];
                saltGenerator.GetBytes(salt);
                return salt;
            }
        }

        /// <summary>
        /// Computes the has value of both the password and salt from the GenerateSalt() method.
        /// </summary>
        /// <param name="password">This is the user given Password</param>
        /// <param name="salt">This is the Salt from GenerateSalt</param>
        /// <param name="iterartions">This is a constant</param>
        /// <param name="hashByteSize">This is a constant</param>
        /// <returns>Byte[] hash</returns>
        internal static byte[] ComputeHash(string password, byte[] salt, int iterartions = HasingIterationsCount, int hashByteSize = HashByteSize)
        {
            using (Rfc2898DeriveBytes hashGenerator = new(password, salt))
            {
                hashGenerator.IterationCount = iterartions;
                return hashGenerator.GetBytes(hashByteSize);
            }
        }

        /// <summary>
        /// This calls the ComputeHash() using the password just given by the user and the salt Generated at
        /// account creation, this ensures that the is the password is the same as the one given at account creation
        /// the byte[] should be the same as that on the db.
        /// </summary>
        /// <param name="password">user given password at this time</param>
        /// <param name="passwordSalt">salt from db</param>
        /// <param name="passwordHash">hash from db</param>
        /// <returns></returns>
        internal static bool Verify(string password, byte[] passwordSalt, byte[] passwordHash)
        {
            byte[] computedHash = ComputeHash(password, passwordSalt);
            return AreHashesEqual(computedHash, passwordHash);
        }

        /// <summary>
        /// This ensures the smallest hash lenght is set as minHashLenght.
        /// It then performs an XOR operation on the two hashes to ensure they are the same on the bitwise level.
        /// 
        /// </summary>
        /// <param name="firstHash"></param>
        /// <param name="secondHash"></param>
        /// <returns></returns>
        private static bool AreHashesEqual(byte[] firstHash, byte[] secondHash)
        {
            int minHashLenght = firstHash.Length <= secondHash.Length ? firstHash.Length : secondHash.Length;
            var xor = firstHash.Length ^ secondHash.Length;
            for (int i = 0; i < minHashLenght; i++)
                xor |= firstHash[i] ^ secondHash[i];
            return 0 == xor;
        }
    }
}
