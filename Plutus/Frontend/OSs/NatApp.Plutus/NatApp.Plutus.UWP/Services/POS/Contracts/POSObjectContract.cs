using System.Threading.Tasks;
using Windows.Devices.PointOfService;

namespace NatApp.Plutus.UWP.Services.POS.Contracts
{
    public abstract class POSObjectContract<T> where T : class
    {
        #region Fields
        protected readonly string DeviceId;
        #endregion

        public POSObjectContract(string deviceId)
        {
            DeviceId = deviceId;
        }

        public abstract Task CreatePOSObject();

        public abstract Task InitPOSObject();

        protected abstract Task<T> GetFirstPOSObjectAsync(PosConnectionTypes posConnectionTypes = PosConnectionTypes.All);

        public abstract void Dispose();
    }
}
