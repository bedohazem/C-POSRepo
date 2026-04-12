using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace POS_System.printing
{
    public static class BarcodeHelper
    {
        public static BitmapSource GenerateCode128(string text, int width = 150, int height = 34)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Barcode text is required.", nameof(text));

            var writer = new ZXing.BarcodeWriterPixelData
            {
                Format = ZXing.BarcodeFormat.CODE_128,
                Options = new ZXing.Common.EncodingOptions
                {
                    Width = width,
                    Height = height,
                    Margin = 0,
                    PureBarcode = true
                }
            };

            var pixelData = writer.Write(text.Trim());

            var bitmap = BitmapSource.Create(
                pixelData.Width,
                pixelData.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixelData.Pixels,
                pixelData.Width * 4);

            bitmap.Freeze();
            return bitmap;
        }
    }
}