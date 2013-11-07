using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AForge.Imaging;

namespace OCRLessonReport.Imaging
{
    /// <summary>
    /// delete later if no mod needs
    /// </summary>
    public class HoughLineTransformation
    {
        // Hough transformation quality settings
        private int stepsPerDegree;
        private int houghHeight;
        private double thetaStep;

        //precalculated Sin and Cos values
        private double[]﻿ sinMap;
        private double[]﻿ cosMap;

        // Hough map
        private short[,] houghMap;
        private short maxMapIntensity = 0;

        private int ﻿localPeakRadius = 4;
        private short minLineIntensity = 10;
        private ArrayList lines = new ArrayList();

        /// <summary>
        /// Steps per degree.
        /// </summary>
        /// 
        /// <remarks><para>The value defines quality of Hough line transformation and its ability to detect
        /// lines' slope precisely.</para>
        /// 
        /// <para>Default value is set to <b>1</b>. Minimum value is <b>1</b>. Maximum value is <b>10</b>.</para></remarks>
        /// 
        public int StepsPerDegree
        {
            get { return stepsPerDegree; }
            set
            {
                stepsPerDegree = Math.Max(1, Math.Min(10, value));
                houghHeight = 180 * stepsPerDegree;
                thetaStep = Math.PI / houghHeight;

                // precalculate Sine and Cosine values
                sinMap = new double[houghHeight];
                cosMap = new double[houghHeight];

                for (int i = 0; i < houghHeight; i++)
                {
                    sinMap[i] = Math.Sin(i * thetaStep);
                    cosMap[i] = Math.Cos(i * thetaStep);
                }
            }
        }

        /// <summary>
        /// Minimum <see cref="HoughLine.Intensity">line's intensity</see> in Hough map to recognize a line.
        /// </summary>
        ///
        /// <remarks><para>The value sets minimum intensity level for a line. If a value in Hough
        /// map has lower intensity, then it is not treated as a line.</para>
        /// 
        /// <para>Default value is set to <b>10</b>.</para></remarks>
        ///
        public short MinLineIntensity
        {
            get { return minLineIntensity; }
            set { minLineIntensity = value; }
        }

        /// <summary>
        /// Radius for searching local peak value.
        /// </summary>
        /// 
        /// <remarks><para>The value determines radius around a map's value, which is analyzed to determine
        /// if the map's value is a local maximum in specified area.</para>
        /// 
        /// <para>Default value is set to <b>4</b>. Minimum value is <b>1</b>. Maximum value is <b>10</b>.</para></remarks>
        /// 
        public int LocalPeakRadius
        {
            get { return localPeakRadius; }
            set { localPeakRadius = Math.Max(1, Math.Min(10, value)); }
        }

        /// <summary>
        /// Maximum found <see cref="HoughLine.Intensity">intensity</see> in Hough map.
        /// </summary>
        /// 
        /// <remarks><para>The property provides maximum found line's intensity.</para></remarks>
        /// 
        public short MaxIntensity
        {
            get { return maxMapIntensity; }
        }

        /// <summary>
        /// Found lines count.
        /// </summary>
        /// 
        /// <remarks><para>The property provides total number of found lines, which intensity is higher (or equal to),
        /// than the requested <see cref="MinLineIntensity">minimum intensity</see>.</para></remarks>
        /// 
        public int LinesCount
        {
            get { return lines.Count; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HoughLineTransformation"/> class.
        /// </summary>
        /// 
        public HoughLineTransformation()
        {
            StepsPerDegree = 1;
        }

        /// <summary>
        /// Process an image building Hough map.
        /// </summary>
        /// 
        /// <param name="image">Source image to process.</param>
        /// 
        /// <exception cref="UnsupportedImageFormatException">Unsupported pixel format of the source image.</exception>
        /// 
        public void ProcessImage(Bitmap image, HoughLineRequestSettings settings = null)
        {
            ProcessImage(image, new Rectangle(0, 0, image.Width, image.Height), settings);
        }

        /// <summary>
        /// Process an image building Hough map.
        /// </summary>
        /// 
        /// <param name="image">Source image to process.</param>
        /// <param name="rect">Image's rectangle to process.</param>
        /// 
        /// <exception cref="UnsupportedImageFormatException">Unsupported pixel format of the source image.</exception>
        /// 
        public void ProcessImage(Bitmap image, Rectangle rect, HoughLineRequestSettings settings = null)
        {
            // check image format
            if (image.PixelFormat != PixelFormat.Format8bppIndexed)
            {
                throw new UnsupportedImageFormatException("Unsupported pixel format of the source image.");
            }

            // lock source image
            BitmapData imageData = image.LockBits(
                new Rectangle(0, 0, image.Width, image.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);

            try
            {
                // process the image
                ProcessImage(new UnmanagedImage(imageData), rect, settings);
            }
            finally
            {
                // unlock image
                image.UnlockBits(imageData);
            }
        }

        /// <summary>
        /// Process an image building Hough map.
        /// </summary>
        /// 
        /// <param name="imageData">Source image data to process.</param>
        /// 
        /// <exception cref="UnsupportedImageFormatException">Unsupported pixel format of the source image.</exception>
        /// 
        public void ProcessImage(BitmapData imageData, HoughLineRequestSettings settings = null)
        {
            ProcessImage(new UnmanagedImage(imageData),
                new Rectangle(0, 0, imageData.Width, imageData.Height), settings);
        }

        /// <summary>
        /// Process an image building Hough map.
        /// </summary>
        /// 
        /// <param name="imageData">Source image data to process.</param>
        /// <param name="rect">Image's rectangle to process.</param>
        /// 
        /// <exception cref="UnsupportedImageFormatException">Unsupported pixel format of the source image.</exception>
        /// 
        public void ProcessImage(BitmapData imageData, Rectangle rect, HoughLineRequestSettings settings = null)
        {
            ProcessImage(new UnmanagedImage(imageData), rect, settings);
        }

        /// <summary>
        /// Process an image building Hough map.
        /// </summary>
        /// 
        /// <param name="image">Source unmanaged image to process.</param>
        /// 
        /// <exception cref="UnsupportedImageFormatException">Unsupported pixel format of the source image.</exception>
        /// 
        public void ProcessImage(UnmanagedImage image)
        {
            ProcessImage(image, new Rectangle(0, 0, image.Width, image.Height));
        }

        /// <summary>
        /// Process an image building Hough map.
        /// </summary>
        /// 
        /// <param name="image">Source unmanaged image to process.</param>
        /// <param name="rect">Image's rectangle to process.</param>
        /// 
        /// <exception cref="UnsupportedImageFormatException">Unsupported pixel format of the source image.</exception>
        /// 
        public void ProcessImage(UnmanagedImage image, Rectangle rect, HoughLineRequestSettings settings = null)
        {
            if (image.PixelFormat != PixelFormat.Format8bppIndexed)
            {
                throw new UnsupportedImageFormatException("Unsupported pixel format of the source image.");
            }

            // get source image size
            int width = image.Width;
            int height = image.Height;
            int halfWidth = width / 2;
            int halfHeight = height / 2;

            // make sure the specified rectangle recides with the source image
            rect.Intersect(new Rectangle(0, 0, width, height));

            int startX = -halfWidth + rect.Left;
            int startY = -halfHeight + rect.Top;
            int stopX = width - halfWidth - (width - rect.Right);
            int stopY = height - halfHeight - (height - rect.Bottom);

            int offset = image.Stride - rect.Width;

            // calculate Hough map's width
            int halfHoughWidth = (int)Math.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);
            int houghWidth = halfHoughWidth * 2;

            houghMap = new short[houghHeight, houghWidth];            

            // do the job
            unsafe
            {
                byte* src = (byte*)image.ImageData.ToPointer() +
                    rect.Top * image.Stride + rect.Left;

                // for each row
                for (int y = startY; y < stopY; y++)
                {
                    // for each pixel
                    for (int x = startX; x < stopX; x++, src++)
                    {
                        if (*src > 0)
                        {
                            // for each Theta value
                            for (int theta = 0; theta < houghHeight; theta++)
                            {
                                if (settings != null && !settings.AllowedThetas.Contains(theta))
                                    continue;
                                
                                int radius = (int)Math.Round(cosMap[theta] * x - sinMap[theta] * y) + halfHoughWidth;

                                if ((radius < 0) || (radius >= houghWidth))
                                    continue;

                                houghMap[theta, radius]++;
                            }
                        }
                    }
                    src += offset;
                }
            }

            // find max value in Hough map
            maxMapIntensity = 0;
            for (int i = 0; i < houghHeight; i++)
            {
                for (int j = 0; j < houghWidth; j++)
                {
                    if (houghMap[i, j] > maxMapIntensity)
                    {
                        maxMapIntensity = houghMap[i, j];
                    }
                }
            }

            CollectLines();
        }

        /// <summary>
        /// Convert Hough map to bitmap. 
        /// </summary>
        /// 
        /// <returns>Returns 8 bppp grayscale bitmap, which shows Hough map.</returns>
        /// 
        /// <exception cref="ApplicationException">Hough transformation was not yet done by calling
        /// ProcessImage() method.</exception>
        /// 
        public Bitmap ToBitmap()
        {
            // check if Hough transformation was made already
            if (houghMap == null)
            {
                throw new ApplicationException("Hough transformation was not done yet.");
            }

            int width = houghMap.GetLength(1);
            int height = houghMap.GetLength(0);

            // create new image
            Bitmap image = AForge.Imaging.Image.CreateGrayscaleImage(width, height);

            // lock destination bitmap data
            BitmapData imageData = image.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadWrite, PixelFormat.Format8bppIndexed);

            int offset = imageData.Stride - width;
            float scale = 255.0f / maxMapIntensity;

            // do the job
            unsafe
            {
                byte* dst = (byte*)imageData.Scan0.ToPointer();

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++, dst++)
                    {
                        *dst = (byte)System.Math.Min(255, (int)(scale * houghMap[y, x]));
                    }
                    dst += offset;
                }
            }

            // unlock destination images
            image.UnlockBits(imageData);

            return image;
        }

        /// <summary>
        /// Get specified amount of lines with highest <see cref="HoughLine.Intensity">intensity</see>.
        /// </summary>
        /// 
        /// <param name="count">Amount of lines to get.</param>
        /// 
        /// <returns>Returns array of most intesive lines. If there are no lines detected,
        /// the returned array has zero length.</returns>
        /// 
        public HoughLine[] GetMostIntensiveLines(int count)
        {
            // lines count
            int n = Math.Min(count, lines.Count);

            // result array
            HoughLine[] dst = new HoughLine[n];
            lines.CopyTo(0, dst, 0, n);

            return dst;
        }

        /// <summary>
        /// Get lines with <see cref="HoughLine.RelativeIntensity">relative intensity</see> higher then specified value.
        /// </summary>
        /// 
        /// <param name="minRelativeIntensity">Minimum relative intesity of lines.</param>
        /// 
        /// <returns>Returns array of lines. If there are no lines detected,
        /// the returned array has zero length.</returns>
        /// 
        public HoughLine[] GetLinesByRelativeIntensity(double minRelativeIntensity)
        {
            int count = 0, n = lines.Count;

            while ((count < n) && (((HoughLine)lines[count]).RelativeIntensity >= minRelativeIntensity))
                count++;

            return GetMostIntensiveLines(count);
        }


        // Collect lines with intesities greater or equal then specified
        private void CollectLines()
        {
            int maxTheta = houghMap.GetLength(0);
            int maxRadius = houghMap.GetLength(1);

            short intensity;
            bool foundGreater;

            int halfHoughWidth = maxRadius >> 1;

            // clean lines collection
            lines.Clear();

            // for each Theta value
            for (int theta = 0; theta < maxTheta; theta++)
            {
                // for each Radius value
                for (int radius = 0; radius < maxRadius; radius++)
                {
                    // get current value
                    intensity = houghMap[theta, radius];

                    if (intensity < minLineIntensity)
                        continue;

                    foundGreater = false;

                    // check neighboors
                    for (int tt = theta - localPeakRadius, ttMax = theta + localPeakRadius; tt < ttMax; tt++)
                    {
                        // break if it is not local maximum
                        if (foundGreater == true)
                            break;

                        int cycledTheta = tt;
                        int cycledRadius = radius;

                        // check limits
                        if (cycledTheta < 0)
                        {
                            cycledTheta = maxTheta + cycledTheta;
                            cycledRadius = maxRadius - cycledRadius;
                        }
                        if (cycledTheta >= maxTheta)
                        {
                            cycledTheta -= maxTheta;
                            cycledRadius = maxRadius - cycledRadius;
                        }

                        for (int tr = cycledRadius - localPeakRadius, trMax = cycledRadius + localPeakRadius; tr < trMax; tr++)
                        {
                            // skip out of map values
                            if (tr < 0)
                                continue;
                            if (tr >= maxRadius)
                                break;

                            // compare the neighboor with current value
                            if (houghMap[cycledTheta, tr] > intensity)
                            {
                                foundGreater = true;
                                break;
                            }
                        }
                    }

                    // was it local maximum ?
                    if (!foundGreater)
                    {
                        // we have local maximum
                        lines.Add(new HoughLine((double)theta / stepsPerDegree, (short)(radius - halfHoughWidth), intensity, (double)intensity / maxMapIntensity));
                    }
                }
            }

            lines.Sort();
        }
    }
}
