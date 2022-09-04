using System.Threading.Tasks;
using Windows.Devices.PointOfService;

namespace Plutus.Frontend.ClientUI.Services.PosHandeling.Contracts
{
    public abstract class PosObjectContract<T> where T : class
    {
        #region Fields
        protected readonly string DeviceId;
        #endregion

        public PosObjectContract(string deviceId)
        {
            DeviceId = deviceId;
        }

        public abstract Task CreatePOSObject();

        public abstract Task InitPOSObject();

        protected abstract Task<T> GetFirstPOSObjectAsync(PosConnectionTypes posConnectionTypes = PosConnectionTypes.All);

        public abstract void Dispose();
    }
}
