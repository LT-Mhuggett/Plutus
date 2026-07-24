using CommonPOSLibrary.Enums;
using CommonPOSLibrary.Exceptions;
using Plutus.Frontend.AppClient.Platforms.Windows.Helpers;
using Plutus.Frontend.AppClient.Platforms.Windows.Services.POS.Contracts;
using System;
using System.Threading.Tasks;
using Windows.Devices.PointOfService;

namespace Plutus.Frontend.AppClient.Platforms.Windows.Services.POS
{
    public class POSCashDrawer : POSObjectContract<CashDrawer>
    {
        #region Fields
        private CashDrawer _cashDrawer;
        private ClaimedCashDrawer _claimedCashDrawer;
        #endregion

        public POSCashDrawer(string deviceId = null) : base(deviceId)
        {

        }

        public override async Task CreatePOSObject()
        {
            if (!string.IsNullOrEmpty(DeviceId))
            {
                _cashDrawer = await CashDrawer.FromIdAsync(DeviceId);
                if (_cashDrawer == null)
                {
                    throw new POSObjectException(POSObjectExceptionType.NotFound, POSTargetObjectType.CashDrawer, $"Cash Drawer with Id: {DeviceId}, not found.");
                }
            }
            else
            {
                _cashDrawer = await GetFirstPOSObjectAsync();
                if (_cashDrawer == null)
                {
                    throw new POSObjectException(POSObjectExceptionType.NotFound, POSTargetObjectType.CashDrawer, $"No Cash Drawer has been found.");
                }
            }
        }

        public override async Task InitPOSObject()
        {
            if (_cashDrawer.Status.StatusKind == CashDrawerStatusKind.Online)
            {
                _claimedCashDrawer = await _cashDrawer.ClaimDrawerAsync();
                if (_claimedCashDrawer != null)
                {
                    if (!await _claimedCashDrawer.EnableAsync())
                    {
                        _claimedCashDrawer.Dispose();
                        throw new POSObjectException(POSObjectExceptionType.NotEnableable, POSTargetObjectType.CashDrawer, $"Cash Drawer with Id: {DeviceId}, is not currently enableable.");
                    }
                }
                throw new POSObjectException(POSObjectExceptionType.NotClaimable, POSTargetObjectType.CashDrawer, $"Caash Drawer with Id: {DeviceId}, is currently in use by another process. Please wait.");
            }
            throw new POSObjectException(POSObjectExceptionType.OffOrOffline, POSTargetObjectType.CashDrawer, $"Cash Drawer with Id: {DeviceId}, is off/offline.");
        }

        protected override async Task<CashDrawer> GetFirstPOSObjectAsync(PosConnectionTypes posConnectionTypes = PosConnectionTypes.All)
        {
            return await DeviceHelpers.GetFirstDeviceAsync(CashDrawer.GetDeviceSelector(posConnectionTypes), async (id) => await CashDrawer.FromIdAsync(id));
        }

        #region Operations
        public async Task<bool> OpenCashDrawerAsync()
        {
            if (!_claimedCashDrawer.IsDrawerOpen)
            {
                return await _claimedCashDrawer.OpenDrawerAsync();
            }

            return true;
        }
        #endregion

        public override void Dispose()
        {
            _cashDrawer.Dispose();
        }
    }
}
