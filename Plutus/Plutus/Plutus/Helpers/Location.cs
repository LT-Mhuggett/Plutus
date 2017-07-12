using Plugin.Geolocator;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms.Maps;
using Xamarin.Forms;

namespace Plutus.Helpers
{
    class Location
    {
        public static Geocoder geoCoder;
        public static string messageErr;
        public static async Task<List<string>> ReverseGeocde()
        {
            try
            {
                geoCoder = new Geocoder();
                var location = CrossGeolocator.Current;
                location.DesiredAccuracy = 50;
                var position = await location.GetPositionAsync();
                double? latitude = Convert.ToDouble(position.Latitude);
                double? longitude = Convert.ToDouble(position.Longitude);

                var revPosition = new Position(latitude.Value, longitude.Value);
                IEnumerable<string> possibleAddresses = await geoCoder.GetAddressesForPositionAsync(revPosition);
                var addressList = new List<string>();
                foreach (var address in possibleAddresses)
                    addressList.Add(address);
                return addressList;
            }
            catch (Exception ex)
            {
                messageErr = $"Unable to get GPS Location: {ex}";
                return null;
            }
        }
    }
}
