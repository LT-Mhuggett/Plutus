using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;

namespace Plutus.Helpers
{
    class Password
    {
        private const int SaltByteSize = 64;
        private const int HashByteSize = 64;
        private const int HasingIterationsCount = 101010;

        internal static byte[] GenerateSalt(int saltByteSize = SaltByteSize)
        {
            #if __MOBILE__
            using (RNGCryptoServiceProvider saltGenerator = new RNGCryptoServiceProvider())
            {
                byte[] salt = new byte[saltByteSize];
                saltGenerator.GetBytes(salt);
                return salt;
            }

            #else
            byte[] salt = new byte[saltByteSize];
            RandomNumberGenerator.Create().GetBytes(salt);
            return salt;
            #endif
        }

        internal static byte[] ComputeHash(string password, byte[] salt, int iterartions = HasingIterationsCount, int hashByteSize = HashByteSize)
        {
            using (Rfc2898DeriveBytes hashGenerator = new Rfc2898DeriveBytes(password, salt))
            {
                hashGenerator.IterationCount = iterartions;
                return hashGenerator.GetBytes(hashByteSize);
            }
        }

        internal static bool Verify(string password, byte[] passwordSalt, byte[] passwordHash)
        {
            byte[] computedHash = ComputeHash(password, passwordSalt);
            return AreHashesEqual(computedHash, passwordHash);
        }

        private static bool AreHashesEqual(byte[] firstHash, byte[] secondHash)
        {
            int minHashLenght = firstHash.Length <= secondHash.Length ? firstHash.Length : secondHash.Length;
            var xor = firstHash.Length ^ secondHash.Length;
            for(int i=0; i < minHashLenght; i++)
                xor |= firstHash[i] ^ secondHash[i];
            return 0 == xor;
        }
        public bool Encrypt() {
            



            return true;
        }
    }
}
