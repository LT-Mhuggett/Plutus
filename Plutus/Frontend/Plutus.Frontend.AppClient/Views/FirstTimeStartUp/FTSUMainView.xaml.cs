using System;
using Microsoft.Maui.Devices;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.FirstTimeStartUp
{
    public partial class FTSUMainView : TabbedPage
    {
        /// <summary>
        /// First run.
        ///
        /// ⚠ THE ORDER CHANGED 2026-08-08, and it is the whole point (Matt: *"The till is moving to
        /// the portal being the 1st place you start, not the local app"*). **Connect to Plutus is
        /// first and is the default tab.** A till is provisioned in the portal — company, store,
        /// till, enrolment code — and the app's job on first run is to claim that identity, not to
        /// invent one locally.
        ///
        /// ⚠ Setup and Third-party transfer are LEGACY and are kept only so an existing standalone
        /// install can still be reached while the portal-first path is finished. They are retitled
        /// so nobody picks one by accident: **Setup produces a till that looks configured and can
        /// never talk to the platform** — a locally-invented store and admin, with no tenant, no
        /// till record and no device credential. See till-design Part B.
        /// </summary>
        public FTSUMainView()
        {
            InitializeComponent();

            // Portal-first: this is the path everyone should take.
            Children.Add(new Plutus.Frontend.AppClient.Views.Platform.ConnectionView(firstRun: true));
            Children.Add(new RecoveryView());

            if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
            {
                Children.Add(new SetupView());
                Children.Add(new TransferThirdPartyView());
            }
        }
    }
}