using Plutus.Frontend.AppClient.Helpers.Security;

namespace Plutus.Frontend.AppClient.Tests.Helpers
{
    public class PasswordTests
    {
        [Fact]
        public void GenerateSalt_DefaultSize_Returns64Bytes()
        {
            Assert.Equal(64, Password.GenerateSalt().Length);
        }

        [Fact]
        public void GenerateSalt_CustomSize_ReturnsRequestedLength()
        {
            Assert.Equal(16, Password.GenerateSalt(16).Length);
        }

        [Fact]
        public void GenerateSalt_ProducesDifferentValuesEachCall()
        {
            Assert.NotEqual(Password.GenerateSalt(), Password.GenerateSalt());
        }

        [Fact]
        public void ComputeHash_IsDeterministic_ForSamePasswordAndSalt()
        {
            var salt = Password.GenerateSalt();
            var hash1 = Password.ComputeHash("Passw0rd!", salt);
            var hash2 = Password.ComputeHash("Passw0rd!", salt);
            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void ComputeHash_DiffersForDifferentPasswords()
        {
            var salt = Password.GenerateSalt();
            Assert.NotEqual(Password.ComputeHash("Passw0rd!", salt), Password.ComputeHash("Different!", salt));
        }

        [Fact]
        public void ComputeHash_DefaultSize_Returns64Bytes()
        {
            Assert.Equal(64, Password.ComputeHash("Passw0rd!", Password.GenerateSalt()).Length);
        }

        [Fact]
        public void Verify_CorrectPassword_ReturnsTrue()
        {
            var salt = Password.GenerateSalt();
            var hash = Password.ComputeHash("Passw0rd!", salt);
            Assert.True(Password.Verify("Passw0rd!", salt, hash));
        }

        [Fact]
        public void Verify_WrongPassword_ReturnsFalse()
        {
            var salt = Password.GenerateSalt();
            var hash = Password.ComputeHash("Passw0rd!", salt);
            Assert.False(Password.Verify("WrongPassword!", salt, hash));
        }

        [Fact]
        public void Verify_DifferentLengthHash_ReturnsFalse()
        {
            var salt = Password.GenerateSalt();
            var shorterHash = Password.ComputeHash("Passw0rd!", salt, hashByteSize: 16);
            Assert.False(Password.Verify("Passw0rd!", salt, shorterHash));
        }
    }
}
