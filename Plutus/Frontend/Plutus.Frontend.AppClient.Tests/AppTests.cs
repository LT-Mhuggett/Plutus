using System.Reflection;
using Moq;
using Plutus.Frontend.AppClient.Services.Loading;

namespace Plutus.Frontend.AppClient.Tests
{
    /// <summary>
    /// App itself can't be constructed (Application is BindableObject-derived - see
    /// ValidationGroupBehaviorTests), but its static loading-state helpers are reachable directly.
    /// IsLoading is a private static field shared by the whole test assembly, so every test here resets
    /// it via reflection afterwards rather than through the public API - SetLoading(false)/ToggleLoading
    /// would themselves call GetViewModel() on the "hide" path and throw (see below), which is exactly
    /// the behavior GetViewModel_WithoutRealApp_ThrowsNullReferenceException documents.
    /// </summary>
    public class AppTests
    {
        private static void ResetIsLoading()
        {
            typeof(App).GetField("IsLoading", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
        }

        [Fact]
        public void GetViewModel_WithoutRealApp_ThrowsNullReferenceException()
        {
            // _app is only ever assigned inside App's own constructor, which can't run here.
            Assert.Throws<NullReferenceException>(() => App.GetViewModel());
        }

        [Fact]
        public void SetLoading_ToSameValue_IsANoOp()
        {
            ResetIsLoading();
            var loadingService = new Mock<ILoadingViewService>(MockBehavior.Strict);
            TestServices.LoadingViewService = loadingService.Object;

            App.SetLoading(false); // already false - short-circuits before touching ILoadingViewService

            loadingService.VerifyNoOtherCalls();
        }

        [Fact]
        public void SetLoading_True_ShowsLoadingPage()
        {
            ResetIsLoading();
            try
            {
                var loadingService = new Mock<ILoadingViewService>();
                TestServices.LoadingViewService = loadingService.Object;

                App.SetLoading(true);

                loadingService.Verify(l => l.ShowLoadingPage(), Times.Once);
            }
            finally
            {
                ResetIsLoading();
            }
        }

        [Fact]
        public void ToggleLoading_FromFalse_ShowsLoadingPage()
        {
            ResetIsLoading();
            try
            {
                var loadingService = new Mock<ILoadingViewService>();
                TestServices.LoadingViewService = loadingService.Object;

                App.ToggleLoading();

                loadingService.Verify(l => l.ShowLoadingPage(), Times.Once);
            }
            finally
            {
                ResetIsLoading();
            }
        }
    }
}
