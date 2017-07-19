using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Plutus.Models;
using Xamarin.Forms;
using System.IO;
using Plugin.Media;

namespace Plutus.Helpers
{
    class Camera
    {
        internal static async void getPhoto(ItemModel item, ImageCell image)
        {
            var file = await CrossMedia.Current.TakePhotoAsync(new Plugin.Media.Abstractions.StoreCameraMediaOptions
            {
                PhotoSize = Plugin.Media.Abstractions.PhotoSize.Medium,
                Directory = "Items",
                Name = item.ItemId+"image.jpg"
            });

            if (file == null)
                return;

            image.ImageSource = ImageSource.FromStream(() =>
            {
                Debug.Assert(file != null, "file != null", "In Camera.cs");
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
    }
}
