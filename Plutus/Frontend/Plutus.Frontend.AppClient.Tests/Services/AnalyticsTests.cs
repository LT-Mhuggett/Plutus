using Moq;
using Plutus.Frontend.AppClient.Services.Analytics;

namespace Plutus.Frontend.AppClient.Tests.Services
{
    public class AppStateTests
    {
        [Fact]
        public void GetAppLogLevel_DefaultsToVerbose()
        {
            Assert.Equal(AppLogLevel.Verbose, new AppState().GetAppLogLevel());
        }

        [Fact]
        public void SetAppLogLevel_ThenGet_ReturnsSetValue()
        {
            var state = new AppState();
            state.SetAppLogLevel(AppLogLevel.Error);
            Assert.Equal(AppLogLevel.Error, state.GetAppLogLevel());
        }

        [Fact]
        public void GetInstallId_DefaultsToEmptyGuid()
        {
            Assert.Equal(Guid.Empty, new AppState().GetInstallId());
        }
    }

    public class LoggerTests
    {
        [Fact]
        public void LogEvent_BelowThreshold_DoesNotThrow()
        {
            var appState = new Mock<IAppState>();
            appState.Setup(a => a.GetAppLogLevel()).Returns(AppLogLevel.Error);
            TestServices.AppState = appState.Object;

            var logger = new Logger();
            var ex = Record.Exception(() => logger.LogEvent(AppLogLevel.Verbose, "test message"));

            Assert.Null(ex);
            // Observability.LoggerFactory defaults to NullLoggerFactory in the test process (MauiProgram
            // .CreateMauiApp() never runs here), so the underlying Log(...) call is a safe no-op below
            // the threshold check; this asserts the threshold gate itself doesn't throw.
        }

        [Fact]
        public void LogEvent_WithDictionary_BelowThreshold_LeavesDictionaryUntouched()
        {
            var appState = new Mock<IAppState>();
            appState.Setup(a => a.GetAppLogLevel()).Returns(AppLogLevel.None); // gate closed, TrackEvent never called
            TestServices.AppState = appState.Object;

            var logger = new Logger();
            var dictionary = new Dictionary<string, string>();
            logger.LogEvent(AppLogLevel.Info, "test", dictionary);

            Assert.Empty(dictionary); // "InstallId" is only added once the threshold gate is open
        }

        [Fact]
        public void LogEvent_AboveThreshold_DoesNotThrow()
        {
            // Observability.LoggerFactory defaults to NullLoggerFactory in the test process, so the
            // underlying Log(...) call is a safe no-op, letting the gate-open branch - including the
            // TestCloud environment-variable check - run without a live app. IsEmulatorOrSimulator is
            // pinned to false via the mock so this deterministically exercises the gate-open path
            // instead of depending on whether the host happens to be a physical or virtual machine
            // (every hosted CI runner is virtualized, so DeviceInfo.DeviceType would otherwise report
            // Virtual there and silently no-op this call).
            var appState = new Mock<IAppState>();
            appState.Setup(a => a.GetAppLogLevel()).Returns(AppLogLevel.Verbose);
            appState.Setup(a => a.IsEmulatorOrSimulator()).Returns(false);
            TestServices.AppState = appState.Object;

            var logger = new Logger();
            var ex = Record.Exception(() => logger.LogEvent(AppLogLevel.Verbose, "test message"));

            Assert.Null(ex);
        }

        [Fact]
        public void LogEvent_WithDictionary_AboveThreshold_AddsInstallId()
        {
            var appState = new Mock<IAppState>();
            appState.Setup(a => a.GetAppLogLevel()).Returns(AppLogLevel.Verbose);
            appState.Setup(a => a.GetInstallId()).Returns(Guid.Empty);
            appState.Setup(a => a.IsEmulatorOrSimulator()).Returns(false);
            TestServices.AppState = appState.Object;

            var logger = new Logger();
            var dictionary = new Dictionary<string, string>();
            logger.LogEvent(AppLogLevel.Verbose, "test", dictionary);

            Assert.Equal(Guid.Empty.ToString(), dictionary["InstallId"]);
        }
    }
}
