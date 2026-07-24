using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.Enum;
using Plutus.Frontend.ClientUI.Core.EventArgs;
using Plutus.Frontend.ClientUI.Core.Models;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;

namespace Plutus.Frontend.ClientUI.ViewModels.PopupViewModels
{
    public partial class CategoryCreateViewModel : PopupBaseViewModel<Category>
    {
        private IRepositoryWrapper _repositoryWrapper;

        [ObservableProperty]
        private Category _category;

        public CategoryCreateViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService, IRepositoryWrapper repositoryWrapper) : base(logger, appState, loadingViewService)
        {
            _repositoryWrapper = repositoryWrapper;
            Category = new();
        }

        [RelayCommand]
        private async Task CategoryCreate()
        {
            Category.IdTwo = AppState.Business.Id;
            if(!await _repositoryWrapper.CategoryRepository.Create(Category))
            {
                await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.DbIssue, Strings.OK);
                Logger.LogEvent(AppLogLevel.Error, $"{this.GetType().Name}: Category create failed.");
                return;
            }

            await App.Current.MainPage.DisplayAlert(Strings.Success, Strings.Saved, Strings.OK);
            OnCloseRequest(this, new PopupCloseRequestEventArgs<Category>(this, new PopupReturnValue<Category>(PopupReturnStatus.Completed, Category)));
        }

        [RelayCommand]
        private void CancelCategoryCreate()
        {
            OnCloseRequest(this, new PopupCloseRequestEventArgs<Category>(this, new PopupReturnValue<Category>(PopupReturnStatus.Canceled, null)));
        }
    }
}
