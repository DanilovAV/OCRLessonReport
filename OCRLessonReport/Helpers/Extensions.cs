using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AForge.Imaging.Filters;

namespace OCRLessonReport.Helpers
{
    public static class Extensions
    {
        public static Bitmap ToBitmap(this byte[] bytes)
        {
            ImageConverter converter = new ImageConverter();
            Bitmap bitmap = (Bitmap)converter.ConvertFrom(bytes);
            return bitmap;
        }

        public static Bitmap ToBitmap(this BitmapImage image)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(image));
                enc.Save(stream);
                Bitmap bitmap = new Bitmap(stream);
                return bitmap;

            }
       }

        public static BitmapImage ToBitmapImage(this byte[] arr)
        {
            using (MemoryStream stream = new MemoryStream(arr))
            {
                BitmapImage image = new BitmapImage();
                stream.Seek(0, SeekOrigin.Begin);
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                return image;
            }
        }

        public static BitmapImage ToBitmapImage(this Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Jpeg);
                stream.Position = 0;

                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                return image;
            }
        }

        public static Bitmap Copy(this Bitmap srcBitmap, Rectangle section)
        {
            if (section.Width < 1 || section.Height < 1)
                return srcBitmap;

            Crop crop = new Crop(section);
            return crop.Apply(srcBitmap);
        }
    }
}
