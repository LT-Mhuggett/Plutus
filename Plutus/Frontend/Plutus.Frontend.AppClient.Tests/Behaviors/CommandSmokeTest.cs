namespace Plutus.Frontend.AppClient.Tests.Behaviors
{
    public class CommandSmokeTest
    {
        [Fact]
        public void Command_Construction_DoesNotThrow()
        {
            var ex = Record.Exception(() => new Microsoft.Maui.Controls.Command(() => { }));
            Assert.Null(ex);
        }
    }
}
