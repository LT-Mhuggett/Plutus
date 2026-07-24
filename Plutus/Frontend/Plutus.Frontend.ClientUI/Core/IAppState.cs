using AutoMapper;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core.Collections;
using Plutus.Frontend.ClientUI.Services.Analytics;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Plutus.Frontend.ClientUI.Core
{
    public interface IAppState : INotifyPropertyChanged
    {
        Guid InstallId { get; }
        Guid SessionId { get; }
        Guid DeviceVendorId { get; }
        AppLogLevel AppLogLevel { get; }
        ObservableCollection<Employee> LoggedInEmployees { get; }
        ObservableDictionary<Guid, string> Employee_AuthenticationData { get; }
        KeyValuePair<Guid, string> CurrentActiveUser { get; set; }
        Business Business { get; set; }
        Store Store { get; set; }
        Till Till { get; set; }
        IMapper Mapper { get; }

        Task Init();
        void SetAppLogLevel(AppLogLevel level);
    }
}
