using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OCRLessonReport
{    
    public class Settings
    {
        public double VerticalSensitivity { get; set; }
        public double HorizontalSensitivity { get; set; }
        public double CellSensitivity { get; set; }

        public double HeaderYOffset { get; set; }      

        public int FilteringColor { get; set; }

        public int LeftEdgeColorDeviation { get; set; }
        public int RightEdgeColorDeviation { get; set; }

        public int EdgePixelCheckingLength { get; set; }

        public double LeftEdgeOffset { get; set; }
        public double RightEdgeOffset { get; set; }
        public double TopEdgeOffset { get; set; }
        public double BottomEdgeOffset { get; set; }

        public int LineGroupingDelta { get; set; }

        public int HeaderStartLine { get; set; }
        public int BottomStartLine { get; set; }
        public int NameStartLine { get; set; }

        public int ColumnSubjectStart { get; set; }

        public double CellXEdgeWith { get; set; }
        public double CellYEdgeWith { get; set; }

        public int CellEdgeCutting { get; set; }
        public int CellMaskSensitivity { get; set; }
        public int CellColorFilter { get; set; }
        public double CellMarkDetectRadius { get; set; }

        //Increasing brightness
        public int Brightness { get; set; }
        //Tesseract settings
        public string TessdataLanguage { get; set; }
        public string TessdataPath { get; set; }
        public string TessdataPrefix { get; set; }
        //Sheet edge detection sensitivity
        public double SheetEdgeSensitivity { get; set; }
        public int SheetEdgeVerticalMaxAngle { get; set; }
    }   
}