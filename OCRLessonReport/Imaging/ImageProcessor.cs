using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AForge;
using AForge.Imaging;
using AForge.Imaging.Filters;
using AForge.Math.Geometry;
using OCRLessonReport.Helpers;
using OCRLessonReport.Imaging;
using OCRLessonReport.Properties;
using Tesseract;

namespace OCRLessonReport.Imaging
{
    public interface IImageProcessor
    {
        BitmapImage SourceBitmapImage { get; }
        List<TableCell> Cells { get; }
        void ProccessImage(BackgroundWorker worker);      
    }

    public class ImageProcessor : IImageProcessor
    {
        private Bitmap sourceBitmap;
        private readonly Settings Settings;
        private BackgroundWorker worker;

        public ImageProcessor()
        {
        }

        public ImageProcessor(Bitmap sourceBitmap, ISettingsManager settingsManager)
        {
            this.sourceBitmap = sourceBitmap;
            this.Settings = settingsManager.Settings;
        }

        public virtual void ProccessImage(BackgroundWorker worker)
        {
            this.worker = worker;

            UpdateProgress(3);

            //Brightness and sharpen filters
            BrightnessCorrection cfilter = new BrightnessCorrection(Settings.Brightness);
            GaussianSharpen filter = new GaussianSharpen(4, 11);
            //Apply filters
            cfilter.ApplyInPlace(sourceBitmap);
            UpdateProgress(15);
            filter.ApplyInPlace(sourceBitmap);
            UpdateProgress(30);

            //Convert to gray
            var tmpImage = ConvertToGrayScale(sourceBitmap);
            UpdateProgress(35);

            //Cut edges
            tmpImage = CutEdgesAndInvert(tmpImage);
            UpdateProgress(40);

            //Get angle for rotating image
            var rotateAngle = DetectRotation(tmpImage);
            UpdateProgress(45);

            if (rotateAngle != 0)
            {
                RotateBilinear rotate = new RotateBilinear(rotateAngle, true);
                tmpImage = rotate.Apply(tmpImage);
            }

            //Build horizontal hough lines
            OCRStudentsPresence.Imaging.HoughLineTransformation lineTransform = new OCRStudentsPresence.Imaging.HoughLineTransformation();

            HoughLineRequestSettings settings = new HoughLineRequestSettings
            {
                HorizontalLines = true,
                VerticalLines = false,
                HorizontalDeviation = 2
            };

            lineTransform.ProcessImage(tmpImage, settings);

            //Get horizontal line
            HoughLine[] lines = lineTransform.GetLinesByRelativeIntensity(Settings.HorizontalSensitivity);

            //Get half width and height for future calculations
            int hWidth = tmpImage.Width / 2;
            int hHeight = tmpImage.Height / 2;
            //Get line coordinates (Y axis only - horizontal lines)
            var lineCoordinates = lines.Select(line => hHeight - line.Radius);
            //Grouping coords by delta
            var groupedCoordinates = ImagingHelper.GroupingCoordinates(lineCoordinates, Settings.LineGroupingDelta);

            if (groupedCoordinates.Count <= Settings.HeaderStartLine)
                throw new Exception("Invalid source. Can't be recognized");

            int headerLineY0 = groupedCoordinates[Settings.HeaderStartLine];
            int headerLineY1 = groupedCoordinates[Settings.HeaderStartLine + 1];
            //Copy header to new image
            var headerImage = tmpImage.Copy(new Rectangle(0, headerLineY0, tmpImage.Width, headerLineY1 - headerLineY0));
            //Parse header to get header lines
            HoughLineRequestSettings headerSettings = new HoughLineRequestSettings
            {
                HorizontalLines = false,
                VerticalLines = true,
                VerticalDeviation = 1
            };

            lineTransform.ProcessImage(headerImage, headerSettings);

            Func<HoughLine, int, int> getRadius = (l, w) =>
            {
                if (l.Theta > 90 && l.Theta < 180)
                    return w - l.Radius;
                else
                    return w + l.Radius;
            };

            HoughLine[] headerLines = lineTransform.GetLinesByRelativeIntensity(Settings.VerticalSensitivity);
            //Get header vertical lines
            var headerLineCoordinates = headerLines.Select(line => getRadius(line, hWidth));
            //Grouped lines
            var groupedheaderLineCoordinates = ImagingHelper.GroupingCoordinates(headerLineCoordinates, Settings.LineGroupingDelta);
            //Build cell map
            List<TableCell> cellMap = new List<TableCell>();
              
            UpdateProgress(50);

            //Use tess engine for ocr
            using (TesseractEngine engine = new TesseractEngine(Settings.TessdataPath, Settings.TessdataLanguage))
            {
                //Parse top header
                var x0 = groupedheaderLineCoordinates.FirstOrDefault();
                var x1 = groupedheaderLineCoordinates.LastOrDefault();
                var y0 = groupedCoordinates[0];
                var y1 = groupedCoordinates[1];

                int fullProgress = (groupedheaderLineCoordinates.Count - 1) * (groupedCoordinates.Count - Settings.BottomStartLine - 1 - Settings.HeaderStartLine);
                int curProgress = 0;

                var hImage = tmpImage.Copy(new Rectangle(x0, y0, x1 - x0, y1 - y0));
                hImage = ProcessCell(hImage);

                using (var page = engine.Process(hImage, PageSegMode.SingleBlock))
                {
                    cellMap.Add(new TableCell(0, 0, TableCellType.MainHeader, hImage, page.GetText(), false));
                }

                //Parse table
                for (int i = 0; i < groupedheaderLineCoordinates.Count - 1; i++)
                {
                    int subjectArea = (i < Settings.ColumnSubjectStart - 1) ? 0 : 1;

                    for (int j = Settings.HeaderStartLine; j < groupedCoordinates.Count - Settings.BottomStartLine - 1; j++)
                    {
                        int headerArea = (j == Settings.HeaderStartLine) ? 2 : 0;

                        TableCellType cellType = (TableCellType)(subjectArea + headerArea);

                        var cellImg = tmpImage.Copy(new Rectangle(groupedheaderLineCoordinates[i], groupedCoordinates[j],
                                             groupedheaderLineCoordinates[i + 1] - groupedheaderLineCoordinates[i],
                                             groupedCoordinates[j + 1] - groupedCoordinates[j]));

                        if (cellType == TableCellType.Text || cellType == TableCellType.Header || cellType == TableCellType.HeaderRotated)
                        {
                            cellImg = ProcessCell(cellImg, i == Settings.NameStartLine);

                            string text = String.Empty;

                            if (cellType == TableCellType.HeaderRotated)
                                cellImg.RotateFlip(RotateFlipType.Rotate90FlipNone);

                            using (var page = engine.Process(cellImg, PageSegMode.SingleBlock))
                            {
                                text = page.GetText();
                            }


                            cellMap.Add(new TableCell(i, j, cellType, cellImg, text, false));
                        }
                        else
                        {
                            cellImg = ProcessCell(cellImg);

                            BilateralSmoothing bfilter = new BilateralSmoothing();
                            bfilter.KernelSize = 7;
                            bfilter.SpatialFactor = 10;
                            bfilter.ColorFactor = 60;
                            bfilter.ColorPower = 0.5;
                            bfilter.ApplyInPlace(cellImg);

                            cellImg = FilterColors(cellImg, Settings.FilteringColor, ByteColor.Black, ByteColor.White);

                            BlobCounter bcounter = new BlobCounter();
                            bcounter.ProcessImage(cellImg);

                            var blobs = bcounter.GetObjects(cellImg, false);

                            if (blobs.Length < 1)
                                continue;

                            var biggestBlob = blobs.OrderBy(b => b.Area).LastOrDefault();
                            var biggestBlobsImage = biggestBlob.Image.ToManagedImage();

                            cellMap.Add(new TableCell(i, j, cellType, biggestBlobsImage, GetMask(biggestBlobsImage).ToString(), GetMask(biggestBlobsImage)));
                        }

                        curProgress++;
                        double reportProgress = (double)curProgress / (double)fullProgress * 50 + 50;
                        UpdateProgress((int)reportProgress);
                    }
                }
            }

            this.Cells = cellMap;

            UpdateProgress(100);
        }

        protected void UpdateProgress(int progress)
        {
            if (worker != null)
            {
                worker.ReportProgress(progress);
            }
        }

        #region Properties

        public BitmapImage SourceBitmapImage
        {
            get
            {
                return sourceBitmap.ToBitmapImage();
            }
        }

        public List<TableCell> Cells
        {
            get;
            private set;
        }

        #endregion

        #region Infrastructure

        protected virtual Bitmap ProcessCell(Bitmap cellImg, bool check = false)
        {
            //Build horizontal hough lines
            OCRStudentsPresence.Imaging.HoughLineTransformation lineTransform = new OCRStudentsPresence.Imaging.HoughLineTransformation();

            HoughLineRequestSettings vSettings = new HoughLineRequestSettings
            {
                HorizontalLines = false,
                VerticalLines = true
            };

            HoughLineRequestSettings hSettings = new HoughLineRequestSettings
            {
                HorizontalLines = true,
                VerticalLines = false
            };

            lineTransform.ProcessImage(cellImg, vSettings);

            HoughLine[] vLines = lineTransform.GetLinesByRelativeIntensity(Settings.CellSensitivity);

            lineTransform.ProcessImage(cellImg, hSettings);

            HoughLine[] hLines = lineTransform.GetLinesByRelativeIntensity(Settings.CellSensitivity);

            var hWidth = cellImg.Width / 2;
            var hHeight = cellImg.Height / 2;

            var vLineCoordinates = vLines.Select(line => hWidth + line.Radius);
            var hLineCoordinates = hLines.Select(line => hHeight - line.Radius);

            var xEdges = GetCellEdges(vLineCoordinates, cellImg.Width, Settings.CellXEdgeWith, Settings.CellEdgeCutting);
            var yEdges = GetCellEdges(hLineCoordinates, cellImg.Height, Settings.CellYEdgeWith, Settings.CellEdgeCutting);
           
            cellImg = cellImg.Copy(new Rectangle(xEdges.Item1, yEdges.Item1, xEdges.Item2 - xEdges.Item1, yEdges.Item2 - yEdges.Item1));

            //Check central line
            if (check)
                CheckCenterLine(cellImg);

            return cellImg;
        }

        protected virtual void CheckCenterLine(Bitmap image)
        {
            var tmpImage = image.Copy(new Rectangle(0, 0, image.Width, image.Height));

            var tmpImageData = tmpImage.LockBits(new Rectangle(new System.Drawing.Point(0, 0), tmpImage.Size),
                          ImageLockMode.ReadWrite,
                          tmpImage.PixelFormat);

            Drawing.Line(tmpImageData,
               new IntPoint(0, 0),
               new IntPoint(0, tmpImageData.Height),
               Color.White);

            tmpImage.UnlockBits(tmpImageData);

            OCRStudentsPresence.Imaging.HoughLineTransformation lineTransform = new OCRStudentsPresence.Imaging.HoughLineTransformation();

            HoughLineRequestSettings vSettings = new HoughLineRequestSettings
            {
                HorizontalLines = false,
                VerticalLines = true
            };

            var hWidth = tmpImage.Width / 2;
            var hHeight = tmpImage.Height / 2;

            lineTransform.ProcessImage(tmpImage, vSettings);

            HoughLine[] vLines = lineTransform.GetLinesByRelativeIntensity(0.9);

            var vLineCoordinates = vLines.Select(line => hWidth + line.Radius).Where(x => x > 1).OrderBy(x => x).ToArray();

            if (vLineCoordinates.Length > 0)
            {
                var x0 = vLineCoordinates.FirstOrDefault() - 1;
                var x1 = vLineCoordinates.LastOrDefault() + 2;

                if (x1 - x0 > 10)
                    return;

                var imageData = image.LockBits(new Rectangle(new System.Drawing.Point(0, 0), image.Size),
                         ImageLockMode.ReadWrite,
                         image.PixelFormat);

                Drawing.FillRectangle(imageData, new Rectangle(x0, 0, x1 - x0, image.Height), Color.Black);

                image.UnlockBits(imageData);
            }
        }

        protected virtual Tuple<int, int> GetCellEdges(IEnumerable<int> coordinates, int size, double coeff, int cutting)
        {         
            var x0 = coordinates.Where(x => x < size * coeff).OrderBy(x => x).LastOrDefault();
            var x1 = coordinates.Where(x => x > size * (1 - coeff)).OrderBy(x => x).FirstOrDefault();

            x0 += cutting;
            x1 = (x1 != 0) ? x1 : size;
            x1 -= cutting;

            return new Tuple<int, int>(x0, x1);
        }

        protected virtual Bitmap ConvertToGrayScale(Bitmap image)
        {
            Contract.Requires(image != null, "Image can't be null");
            Contract.Requires(image.Width != 0, "Image's width can't be 0");
            Contract.Requires(image.Height != 0, "Image's height can't be 0");

            var width = image.Width;
            var height = image.Height;

            var imageData = image.LockBits(new Rectangle(new System.Drawing.Point(0, 0), image.Size),
                             ImageLockMode.ReadOnly,
                             image.PixelFormat);

            var cnvImage = new Bitmap(width, height, PixelFormat.Format8bppIndexed);

            cnvImage.Palette = ImagingHelper.GetGrayScalePalette();

            var cnvImageData = cnvImage.LockBits(new Rectangle(new System.Drawing.Point(0, 0), cnvImage.Size),
                             ImageLockMode.ReadWrite,
                             cnvImage.PixelFormat);

            var imageStride = imageData.Stride;
            var cnvImageStride = cnvImageData.Stride;

            var imageScan0 = imageData.Scan0;
            var cnvImageScan0 = cnvImageData.Scan0;

            var imagePixelSize = imageStride / width;

            byte[] imageBits = new byte[imageStride * height];
            byte[] cnvImageBits = new byte[cnvImageStride * height];

            try
            {
                Marshal.Copy(imageData.Scan0, imageBits, 0, imageBits.Length);

                for (var y = 0; y < height; y++)
                {
                    var sourceRow = y * imageStride;
                    var resultRow = y * cnvImageStride;

                    for (var x = 0; x < width; x++)
                    {
                        var pixelColor = (byte)(0.3 * imageBits[sourceRow + x * imagePixelSize + 2] + 0.59 * imageBits[sourceRow + x * imagePixelSize + 1] +
                                            0.11 * imageBits[sourceRow + x * imagePixelSize]);

                        //Remove noise and increase contrast
                        if (pixelColor > Settings.FilteringColor)
                            pixelColor = ByteColor.White;

                        cnvImageBits[resultRow + x] = pixelColor;
                    }
                }

                Marshal.Copy(cnvImageBits, 0, cnvImageData.Scan0, cnvImageBits.Length);
            }
            finally
            {
                image.UnlockBits(imageData);
                cnvImage.UnlockBits(cnvImageData);
            }

            return cnvImage;
        }

        protected virtual Bitmap CutEdgesAndInvert(Bitmap image)
        {
            Contract.Requires(image != null, "Image can't be null");
            Contract.Requires(image.Width != 0, "Image's width can't be 0");
            Contract.Requires(image.Height != 0, "Image's height can't be 0");
            Contract.Requires(image.PixelFormat != PixelFormat.Format8bppIndexed, "Supports only 8bit image format");

            var width = image.Width;
            var height = image.Height;

            var imageData = image.LockBits(new Rectangle(new System.Drawing.Point(0, 0), image.Size),
                             ImageLockMode.ReadWrite,
                             image.PixelFormat);

            byte[] imageBits = new byte[imageData.Stride * height];

            var imageStride = imageData.Stride;

            try
            {
                int leftEdge = 0;
                int rightEdge = width;

                int hHeightStride = height / 2 * imageStride;
                int hWidth = imageStride / 2;

                Marshal.Copy(imageData.Scan0, imageBits, 0, imageBits.Length);

                for (var x = hHeightStride; x < hHeightStride + hWidth; x++)
                {
                    int avgColor = 0;

                    for (int j = 0; j < Settings.EdgePixelCheckingLength; j++)
                    {
                        if (x + j < imageBits.Length)
                            avgColor += imageBits[x + j];
                    }

                    avgColor = (int)(avgColor / Settings.EdgePixelCheckingLength);

                    if ((leftEdge == 0) && (ByteColor.White - avgColor) < Settings.LeftEdgeColorDeviation)
                    {
                        leftEdge = x - hHeightStride;
                        break;
                    }
                }

                for (var x = hHeightStride + imageStride; x > hHeightStride + hWidth; x--)
                {
                    int avgColor = 0;

                    for (int j = 0; j < Settings.EdgePixelCheckingLength; j++)
                    {
                        if (x - j > 0)
                            avgColor += imageBits[x - j];
                    }

                    avgColor = (int)(avgColor / Settings.EdgePixelCheckingLength);

                    if ((rightEdge == width) && (ByteColor.White - avgColor) < Settings.LeftEdgeColorDeviation)
                    {
                        rightEdge = x - hHeightStride;
                        break;
                    }
                }

                leftEdge += Settings.EdgePixelCheckingLength + (int)(width * Settings.LeftEdgeOffset);
                rightEdge -= Settings.EdgePixelCheckingLength + (int)(width * Settings.RightEdgeOffset);
                var topEdge = height * Settings.TopEdgeOffset;
                var bottomEdge = height * (1 - Settings.BottomEdgeOffset);

                for (var y = 0; y < height; y++)
                {
                    var row = y * imageStride;

                    var cutRow = y <= topEdge || y >= bottomEdge;

                    for (var x = 0; x < width; x++)
                    {
                        if (cutRow || (x >= 0 && x < leftEdge) ||
                            (x >= rightEdge))
                        {
                            imageBits[row + x] = 255;
                        }
                    }
                }

                //Invert colors
                for (var y = 0; y < height; y++)
                {
                    var row = y * imageStride;

                    for (var x = 0; x < width; x++)
                    {
                        imageBits[row + x] = (byte)(ByteColor.White - imageBits[row + x]);
                    }
                }

                Marshal.Copy(imageBits, 0, imageData.Scan0, imageBits.Length);
            }
            finally
            {
                image.UnlockBits(imageData);
            }

            return image;
        }

        protected virtual Bitmap FilterColors(Bitmap image, int level, byte color1, byte color2)
        {
            var width = image.Width;
            var height = image.Height;

            var imageData = image.LockBits(new Rectangle(new System.Drawing.Point(0, 0), image.Size),
                          ImageLockMode.ReadWrite,
                          image.PixelFormat);

            byte[] imageBits = new byte[imageData.Stride * height];

            var imageStride = imageData.Stride;

            try
            {
                Marshal.Copy(imageData.Scan0, imageBits, 0, imageBits.Length);

                //Invert colors
                for (var i = 0; i < imageBits.Length; i++)
                    if (imageBits[i] < level)
                        imageBits[i] = color1;
                    else
                        imageBits[i] = color2;

                Marshal.Copy(imageBits, 0, imageData.Scan0, imageBits.Length);
            }
            finally
            {
                image.UnlockBits(imageData);
            }

            return image;
        }

        protected virtual bool GetMask(Bitmap image)
        {
            var width = image.Width;
            var height = image.Height;

            var imageData = image.LockBits(new Rectangle(new System.Drawing.Point(0, 0), image.Size),
                          ImageLockMode.ReadWrite,
                          image.PixelFormat);

            byte[] imageBits = new byte[imageData.Stride * height];

            var imageStride = imageData.Stride;

            int? sum = 0;

            try
            {
                Marshal.Copy(imageData.Scan0, imageBits, 0, imageBits.Length);

                sum = imageBits.Sum(p => p);

                Marshal.Copy(imageBits, 0, imageData.Scan0, imageBits.Length);
            }
            finally
            {
                image.UnlockBits(imageData);
            }

            int avg = 0;

            if (sum.HasValue)
                avg = (int)(sum.Value / imageBits.Length);

            return avg > 130;
        }

        protected virtual double DetectRotation(Bitmap image)
        {
            //Hough line transformation
            OCRStudentsPresence.Imaging.HoughLineTransformation lineTransform = new OCRStudentsPresence.Imaging.HoughLineTransformation();

            HoughLineRequestSettings settings = new HoughLineRequestSettings
            {
                HorizontalLines = false,
                VerticalLines = true,
                VerticalDeviation = Settings.SheetEdgeVerticalMaxAngle
            };

            lineTransform.ProcessImage(image, settings);

            HoughLine[] lines = lineTransform.GetLinesByRelativeIntensity(Settings.SheetEdgeSensitivity);

            if (lines.Length < 1)
                throw new Exception("Can't detect left edge");

            Func<HoughLine, double> getAngle = line =>
            {
                double angle = 0;

                if (line.Theta > 90 && line.Radius > 0)
                    angle = 180 - line.Theta;
                else if (line.Theta > 90 && line.Radius < 0)
                    angle = 180 - line.Theta;
                else if (line.Theta < 90 && line.Radius > 0)
                    angle = -line.Theta;
                else if (line.Theta < 90 && line.Radius < 0)
                    angle = -line.Theta;

                return angle;
            };

            double avgAngle = lines.Select(l => getAngle(l)).Average();

            return avgAngle;
        }

        protected virtual void DrawLines(HoughLine[] lines, Bitmap image)
        {
            var imageData = image.LockBits(new Rectangle(new System.Drawing.Point(0, 0), image.Size),
                           ImageLockMode.ReadWrite,
                           image.PixelFormat);
            //int coun = 0;

            foreach (HoughLine line in lines)
            {
                //coun++;
                //if (coun > 1) break;

                // get line's radius and theta values
                int r = line.Radius;
                double t = line.Theta;

                // check if line is in lower part of the image
                if (r < 0)
                {
                    t += 180;
                    r = -r;
                }

                // convert degrees to radians
                t = (t / 180) * Math.PI;

                // get image centers (all coordinate are measured relative
                // to center)
                int w2 = image.Width / 2;
                int h2 = image.Height / 2;

                double x0 = 0, x1 = 0, y0 = 0, y1 = 0;

                if (line.Theta != 0)
                {
                    // none-vertical line
                    x0 = -w2; // most left point
                    x1 = w2;  // most right point

                    // calculate corresponding y values
                    y0 = (-Math.Cos(t) * x0 + r) / Math.Sin(t);
                    y1 = (-Math.Cos(t) * x1 + r) / Math.Sin(t);
                }
                else
                {
                    // vertical line
                    x0 = line.Radius;
                    x1 = line.Radius;

                    y0 = h2;
                    y1 = -h2;
                }

                // draw line on the image
                Drawing.Line(imageData,
                    new IntPoint((int)x0 + w2, h2 - (int)y0),
                    new IntPoint((int)x1 + w2, h2 - (int)y1),
                    Color.Red);
            }

            Func<HoughLine, bool> getLeftLine = l =>
            {
                return (l.Radius < 0 && l.Theta <= Settings.SheetEdgeVerticalMaxAngle) ||
                    (l.Radius > 0 && (180 - l.Theta) <= Settings.SheetEdgeVerticalMaxAngle);
            };

            Func<HoughLine, bool> getRightLine = l =>
            {
                return (l.Radius > 0 && l.Theta <= Settings.SheetEdgeVerticalMaxAngle) ||
                    (l.Radius < 0 && (180 - l.Theta) <= Settings.SheetEdgeVerticalMaxAngle);
            };

            image.UnlockBits(imageData);
        }

        #endregion

    }
}
