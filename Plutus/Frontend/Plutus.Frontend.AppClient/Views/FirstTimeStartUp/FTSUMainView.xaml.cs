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

            // ⚠⚠ RecoveryView IS GONE — L8, 2026-08-23, and this CHANGES THE FIRST-RUN SCREEN. It
            // offered "restore a database onto this till": pick a `Database.db`, copy it in, check it
            // has employees. That was the on-ramp for a shop migrating off NatApp.
            //
            // ⚠ It went because L1 removed the rest of that path — the archive step and the enrolment
            // gate that read it — on Matt's decision of 2026-08-10 that no such migration is planned.
            // Keeping a restore button with no archive behind it would offer half a migration.
            //
            // ⚠ A future NatApp migration needs BOTH rebuilt, and this is the visible half.

            // ⚠ `SetupView` and `TransferThirdPartyView` ARE DELETED (cutover step 21). Setup built
            // a till that LOOKS configured and can never talk to the platform: a locally-invented
            // store and admin, with no tenant, no till record and no device credential — and it sat
            // as a peer tab beside the real one, so picking the wrong tab produced a till that
            // seemed to work until the first sale went nowhere. TransferThirdParty was
            // self-labelled LEGACY and threw from `async void`. Neither has a place now that
            // enrolment is the only way a till comes into existence.
        }
    }
}