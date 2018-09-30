using Microsoft.PointOfService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace POSIntegration.POS
{
    class POSCashDrawer
    {
        CashDrawer cashDrawer;
        PosExplorer _explorer;


        public POSCashDrawer(ref PosExplorer posExplorer)
        {
            _explorer = posExplorer;
            var device = _explorer.GetDevice(DeviceType.CashDrawer);
            cashDrawer = _explorer.CreateInstance(device) as CashDrawer;
        }

        public void OpenCashDrawer()
        {
            cashDrawer.Open();
            cashDrawer.Claim(1000);
            cashDrawer.DeviceEnabled = true;
            if(!cashDrawer.DrawerOpened)
                cashDrawer.OpenDrawer();
            cashDrawer.DeviceEnabled = false;
            cashDrawer.Release();
            cashDrawer.Close();
        }
    }
}
