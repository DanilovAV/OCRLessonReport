using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OCRLessonReport.Helpers
{
    public class ImagingHelper
    {
        public static ColorPalette GetGrayScalePalette()
        {
            using (var bmp = new Bitmap(1, 1, PixelFormat.Format8bppIndexed))
            {
                var cp = bmp.Palette;
                var entries = cp.Entries;
                for (int i = 0; i < entries.Length; i++)
                {
                    entries[i] = Color.FromArgb(i, i, i);
                }
                return cp;
            }
        }

        public static List<int> GroupingCoordinates(IEnumerable<int> coordinates, int delta)
        {
            var sortedCoordinates = coordinates.OrderBy(c => c).ToList();

            var newCoordinates = new List<int>();

            for (int i = 0; i < sortedCoordinates.Count; i++)
            {
                if ((i < (sortedCoordinates.Count - 1)))
                {
                    int j;
                    int sum = sortedCoordinates[i];

                    for (j = 0; j < sortedCoordinates.Count - i - 1; j++)
                    {
                        if ((sortedCoordinates[i + j + 1] - sortedCoordinates[i + j]) <= delta)
                            sum += sortedCoordinates[i + j + 1];
                        else
                            break;
                    }

                    if (j != 0)
                    {
                        int coordinate = sum / (j + 1);
                        newCoordinates.Add(coordinate);
                        i += j;
                        continue;
                    }
                }

                newCoordinates.Add(sortedCoordinates[i]);
            }

            return newCoordinates;
        }
    }

    public static class ByteColor
    {
        public static byte White = 255;
        public static byte Black = 0;
    }
}
