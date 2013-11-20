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
using System.Text.RegularExpressions;

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
            OCRLessonReport.Imaging.HoughLineTransformation lineTransform = new OCRLessonReport.Imaging.HoughLineTransformation();

            HoughLineRequestSettings settings = new HoughLineRequestSettings
            {
                HorizontalLines = true,
                VerticalLines = false,
                HorizontalDeviation = 0
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
            int headerHeight = headerLineY1 - headerLineY0;
            int headerOffset = (int)((double)headerHeight * Settings.HeaderYOffset);
            //Copy header to new image
            var headerImage = tmpImage.Copy(new Rectangle(0, headerLineY0 + headerOffset, tmpImage.Width, headerHeight - 2 * headerOffset));

            HoughLineRequestSettings headerSettings = new HoughLineRequestSettings
            {
                HorizontalLines = false,
                VerticalLines = true,
                VerticalDeviation = 1
            };

            var groupedheaderLineCoordinates = GetLineCoordinates(headerImage, headerSettings, Settings.VerticalSensitivity, Settings.LineGroupingDelta);
            var groupedheaderLineCoordinatesAlt = GetLineCoordinates(headerImage, headerSettings, Settings.VerticalSensitivity, Settings.LineGroupingDelta, true);

            if (groupedheaderLineCoordinates.Count < groupedheaderLineCoordinatesAlt.Count)
                groupedheaderLineCoordinates = groupedheaderLineCoordinatesAlt;

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

                Dictionary<int, TableCellType> columnTypesMap = ParseColumns(tmpImage, groupedheaderLineCoordinates, groupedCoordinates, cellMap, engine);

                List<int> cachedCoordinates = groupedheaderLineCoordinates;

                Dictionary<int, List<int>> columnsMap = new Dictionary<int, List<int>>();
                Dictionary<int, List<int>> rowsMap = new Dictionary<int, List<int>>();

                //Get column map
                for (int i = Settings.HeaderStartLine + 1; i < groupedCoordinates.Count - Settings.BottomStartLine; i++)
                {
                    //Get row image
                    var rowImg = tmpImage.Copy(new Rectangle(0, groupedCoordinates[i], tmpImage.Width,
                                             groupedCoordinates[i + 1] - groupedCoordinates[i]));

                    HoughLineRequestSettings lineSettings = new HoughLineRequestSettings
                    {
                        HorizontalLines = false,
                        VerticalLines = true,
                    };

                    List<int> groupedLineCoordinates;

                    var tmpRowImage = rowImg.Copy();

                    tmpRowImage = FilterColors(tmpRowImage, Settings.CellColorFilter, ByteColor.Black, ByteColor.White);
                    groupedLineCoordinates = GetLineCoordinates(tmpRowImage, lineSettings, Settings.VerticalSensitivity, Settings.LineGroupingDelta);

                    groupedLineCoordinates = UpdateCoordinates(groupedLineCoordinates, cachedCoordinates);

                    cachedCoordinates = groupedLineCoordinates;

                    columnsMap.Add(i - Settings.HeaderStartLine - 1, groupedLineCoordinates);
                }

                //DrawLines(columnsMap[0].Select(x => x).ToList(), tmpImage);

                //sourceBitmap = tmpImage;

                //return;


                cachedCoordinates.Clear();

                //Parse table
                for (int i = 0; i < groupedheaderLineCoordinates.Count - 1; i++)
                {                    
                    if (columnTypesMap[i] == TableCellType.Unknown)
                        continue;

                    //Get row image
                    var colImg = tmpImage.Copy(new Rectangle(groupedheaderLineCoordinates[i], groupedCoordinates[Settings.HeaderStartLine + 1],
                                             groupedheaderLineCoordinates[i + 1] - groupedheaderLineCoordinates[i],
                                             groupedCoordinates.LastOrDefault() - groupedCoordinates[Settings.HeaderStartLine + 1]));

                    HoughLineRequestSettings lineSettings = new HoughLineRequestSettings
                    {
                        HorizontalLines = true,
                        VerticalLines = false,
                    };

                    DrawLines(new List<int>(Enumerable.Range(0, 4)), colImg, true);

                    List<int> groupedLineCoordinates;

                    var tmpRowImage = colImg.Copy();

                    tmpRowImage = FilterColors(tmpRowImage, Settings.CellColorFilter, ByteColor.Black, ByteColor.White);
                    groupedLineCoordinates = GetLineCoordinates(tmpRowImage, lineSettings, Settings.HorizontalSensitivity, Settings.LineGroupingDelta);

                    groupedLineCoordinates = UpdateCoordinates(groupedLineCoordinates, cachedCoordinates);

                    cachedCoordinates = groupedLineCoordinates;


                    #region cell parsing

                    for (int j = 0; j < groupedLineCoordinates.Count - Settings.BottomStartLine; j++)
                    {                    
                        if (columnsMap[j].Count <= i)
                            continue;

                        var cellImg = tmpImage.Copy(new Rectangle(columnsMap[j][i], groupedLineCoordinates[j] + groupedCoordinates[Settings.HeaderStartLine + 1], columnsMap[j][i + 1] - columnsMap[j][i],
                                                        groupedLineCoordinates[j + 1] - groupedLineCoordinates[j]));                   

                        cellImg = ProcessCell(cellImg, i == Settings.NameStartLine);

                        if (columnTypesMap[i] == TableCellType.Text)
                        {
                            string cellText = String.Empty;

                            using (var page = engine.Process(cellImg, PageSegMode.SingleBlock))
                            {
                                cellText = page.GetText();
                            }

                            int val;

                            if ((i < Settings.ColumnSubjectStart - 1) && !Int32.TryParse(Regex.Replace(cellText, @"[\s]", ""), out val))
                            {
                                Median median = new Median();
                                median.ApplyInPlace(cellImg);

                                using (var page = engine.Process(cellImg, PageSegMode.SingleBlock))
                                {
                                    cellText = page.GetText();
                                }
                            }

                            cellMap.Add(new TableCell(i, j + Settings.HeaderStartLine + 1, columnTypesMap[i], cellImg, cellText, false));
                        }
                        else if (columnTypesMap[i] == TableCellType.Mark)
                        {
                            BilateralSmoothing bfilter = new BilateralSmoothing();
                            bfilter.KernelSize = 7;
                            bfilter.SpatialFactor = 10;
                            bfilter.ColorFactor = 60;
                            bfilter.ColorPower = 0.5;
                            bfilter.ApplyInPlace(cellImg);

                            Median median = new Median();
                            median.ApplyInPlace(cellImg);

                            cellImg = FilterColors(cellImg, Settings.FilteringColor, ByteColor.Black, ByteColor.White);

                            BlobCounter bcounter = new BlobCounter();
                            bcounter.ProcessImage(cellImg);

                            var blobs = bcounter.GetObjects(cellImg, false);

                            if (blobs.Length < 1)
                                continue;

                            var biggestBlob = blobs.OrderBy(b => b.Area).LastOrDefault();
                            var biggestBlobsImage = biggestBlob.Image.ToManagedImage();

                            cellMap.Add(new TableCell(i, j + Settings.HeaderStartLine + 1, columnTypesMap[i], biggestBlobsImage, String.Empty, DetectMark(biggestBlobsImage, Settings.CellMaskSensitivity, Settings.CellMarkDetectRadius)));
                        }

                        curProgress++;
                        double reportProgress = (double)curProgress / (double)fullProgress * 50 + 50;

                        UpdateProgress((int)reportProgress);
                    }

                    #endregion
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

        protected virtual List<int> UpdateCoordinates(List<int> coordinates, List<int> cachedCoordinates)
        {
            if (cachedCoordinates.Count < 1)
                return coordinates;

            List<int> removing = new List<int>();

            for (int i = 0; i < coordinates.Count; i++)
            {
                if (cachedCoordinates.Where(c => Math.Abs(c - coordinates[i]) < 10).Count() > 0)
                    continue;

                removing.Add(coordinates[i]);
            }

            coordinates = coordinates.Where(c => !removing.Contains(c)).ToList();

            int j = 0;

            for (int i = 0; i < cachedCoordinates.Count; i++)
            {
                if ((j >= coordinates.Count) || Math.Abs(coordinates[j] - cachedCoordinates[i]) > 10)
                    coordinates.Add(cachedCoordinates[i]);
                else
                    j++;
            }

            return coordinates.OrderBy(c => c).ToList();
        }

        protected virtual Dictionary<int, TableCellType> ParseColumns(Bitmap image, List<int> xCoord, List<int> yCoord, List<TableCell> cellMap, TesseractEngine engine)
        {
            Dictionary<int, TableCellType> columnTypesMap = new Dictionary<int, TableCellType>();

            for (int i = 0; i < xCoord.Count - 1; i++)
            {
                bool isSubjectArea = (i > Settings.ColumnSubjectStart - 1);

                var cellImg = image.Copy(new Rectangle(xCoord[i], yCoord[Settings.HeaderStartLine],
                                               xCoord[i + 1] - xCoord[i],
                                               yCoord[Settings.HeaderStartLine + 1] - yCoord[Settings.HeaderStartLine]));

                string cellText = String.Empty;

                cellImg = ProcessCell(cellImg);

                if (isSubjectArea)
                    cellImg.RotateFlip(RotateFlipType.Rotate90FlipNone);

                BlobCounter bcounter = new BlobCounter();
                bcounter.FilterBlobs = true;
                bcounter.MinHeight = 5;
                bcounter.MinWidth = 5;
                //bcounter.ProcessImage(cellImg);

                ////Check if header cell has something
                //if (bcounter.ObjectsCount < 1)
                //{
                //    columnTypesMap.Add(i, TableCellType.Unknown);
                //    continue;
                //}
                //Try parse header text
                using (var page = engine.Process(cellImg, PageSegMode.SingleBlock))
                {
                    cellText = page.GetText();
                }
                //If can't parse - unknown type
                if (String.IsNullOrWhiteSpace(cellText.Trim()))
                {
                    columnTypesMap.Add(i, TableCellType.Unknown);
                    continue;
                }
                //Check if possible subject area
                if (!isSubjectArea)
                {
                    columnTypesMap.Add(i, TableCellType.Text);
                }
                else
                {
                    var tmpCellImg = image.Copy(new Rectangle(xCoord[i], yCoord[Settings.HeaderStartLine + 1],
                                                        xCoord[i + 1] - xCoord[i],
                                                        yCoord[Settings.HeaderStartLine + 2] - yCoord[Settings.HeaderStartLine + 1]));

                    tmpCellImg = ProcessCell(tmpCellImg);

                    bcounter.ProcessImage(tmpCellImg);

                    if (bcounter.ObjectsCount < 1)
                    {
                        columnTypesMap.Add(i, TableCellType.Unknown);
                        continue;
                    }

                    string tmpCellText;

                    using (var page = engine.Process(tmpCellImg, PageSegMode.SingleBlock))
                    {
                        tmpCellText = page.GetText();
                    }

                    int num;

                    if (Int32.TryParse(tmpCellText.Trim(), out num) && num != 0)
                        columnTypesMap.Add(i, TableCellType.Text);
                    else
                        columnTypesMap.Add(i, TableCellType.Mark);
                }

                cellMap.Add(new TableCell(i, Settings.HeaderStartLine, columnTypesMap[i] | TableCellType.Header, cellImg, cellText, false));
            }

            return columnTypesMap;
        }

        protected List<int> GetLineCoordinates(Bitmap headerImage, HoughLineRequestSettings lineSettings, double sensitivity, int delta, bool useFilter = false)
        {
            OCRLessonReport.Imaging.HoughLineTransformation lineTransform = new HoughLineTransformation();

            int width = 0;

            if (lineSettings.HorizontalLines)
                width = headerImage.Height / 2;
            else if (lineSettings.VerticalLines)
                width = headerImage.Width / 2;

            if (width == 0)
                throw new Exception("Wrong image or settings, must be setted vertical or horizontal setting");

            if (useFilter)
            {
                headerImage = headerImage.Copy(new Rectangle(0, 0, headerImage.Width, headerImage.Height));
                Median median = new Median();
                median.ApplyInPlace(headerImage);
            }

            lineTransform.ProcessImage(headerImage, lineSettings);

            Func<HoughLine, int, int> getRadius = (l, w) =>
            {
                if (l.Theta >= 90 && l.Theta < 180)
                    return w - l.Radius;
                else
                    return w + l.Radius;
            };

            HoughLine[] lines = lineTransform.GetLinesByRelativeIntensity(sensitivity);
            //Get header vertical lines
            var lineCoordinates = lines.Select(line => getRadius(line, width));
            //Grouped lines
            var groupedLineCoordinates = ImagingHelper.GroupingCoordinates(lineCoordinates, delta);

            return groupedLineCoordinates;
        }

        protected virtual Bitmap ProcessCell(Bitmap cellImg, bool check = false)
        {
            //Build horizontal hough lines
            OCRLessonReport.Imaging.HoughLineTransformation lineTransform = new OCRLessonReport.Imaging.HoughLineTransformation();

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

            OCRLessonReport.Imaging.HoughLineTransformation lineTransform = new OCRLessonReport.Imaging.HoughLineTransformation();

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

        protected virtual bool DetectMark(Bitmap image, int color, double detectRadius)
        {
            int width = image.Width;
            int height = image.Height;

            int minSize = (new[] { width, height }).Min();

            int r = (int)(minSize * detectRadius);

            HashSet<IntPoint> points = new HashSet<IntPoint>();

            for (int theta = 0; theta < 360; theta++)
            {
                for (int i = 0; i <= r; i++)
                {
                    double x = i * Math.Cos(theta);
                    double y = -i * Math.Sin(theta);

                    var point = new IntPoint((int)x, (int)y);

                    if (!points.Contains(point))
                        points.Add(point);
                }
            }

            var imageData = image.LockBits(new Rectangle(new System.Drawing.Point(0, 0), image.Size),
                          ImageLockMode.ReadWrite,
                          image.PixelFormat);

            var imageStride = imageData.Stride;

            byte[] imageBits = new byte[imageStride * height];

            try
            {
                Marshal.Copy(imageData.Scan0, imageBits, 0, imageBits.Length);
            }
            finally
            {
                image.UnlockBits(imageData);
            }

            for (int y = r; y < height - r; y++)
            {
                for (int x = r; x < width - r; x++)
                {
                    int sum = 0;

                    foreach (var point in points)
                    {
                        int pX = point.X + x;
                        int pY = point.Y + y;

                        int pixel = pX + pY * imageStride;

                        sum += imageBits[pixel];
                    }

                    int avg = (int)(sum / points.Count);

                    if (avg > color)
                        return true;
                }
            }

            return false;
        }

        protected virtual double DetectRotation(Bitmap image)
        {
            //Hough line transformation
            OCRLessonReport.Imaging.HoughLineTransformation lineTransform = new OCRLessonReport.Imaging.HoughLineTransformation();

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

        protected virtual void DrawLines(List<int> coordinates, Bitmap image, bool horizontal = false)
        {
            var imageData = image.LockBits(new Rectangle(new System.Drawing.Point(0, 0), image.Size),
                   ImageLockMode.ReadWrite,
                   image.PixelFormat);

            foreach (var coord in coordinates)
            {
                if (horizontal)
                {
                    Drawing.Line(imageData, new IntPoint(0, coord), new IntPoint(image.Width, coord), Color.White);
                }
                else
                {
                    Drawing.Line(imageData, new IntPoint(coord, 0), new IntPoint(coord, image.Height), Color.White);
                }
            }

            image.UnlockBits(imageData);
        }

        #endregion

    }
}
