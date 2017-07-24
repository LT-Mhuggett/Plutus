using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Plutus.Models;
using Xamarin.Forms;
using System.IO;
using Plugin.Media;
using Plugin.Media.Abstractions;

namespace Plutus.Helpers
{
    class Camera
    {
        internal static async void getPhoto(ItemModel item, Image image)
        {
            var file = await CrossMedia.Current.TakePhotoAsync(new Plugin.Media.Abstractions.StoreCameraMediaOptions
            {
                PhotoSize = Plugin.Media.Abstractions.PhotoSize.Medium,
                Directory = "Items",
                Name = item.ItemId+"image.jpg"
            });

            if (file == null)
                return;

            image.Source = ImageSource.FromStream(() =>
            {
                var stream = file.GetStream();
                return stream;
            });

            using(var memoryStream = new MemoryStream())
            {
                file.GetStream().CopyTo(memoryStream);
                file.Dispose();
                item.Image = memoryStream.ToArray();
            }
        }

        internal static bool IsCameraAval()
        {
            return CrossMedia.Current.IsCameraAvailable && CrossMedia.Current.IsTakePhotoSupported;
        }

        internal static byte[] StreamToArray(Stream stream)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
