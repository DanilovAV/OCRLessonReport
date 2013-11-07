using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using OCRLessonReport.Helpers;

namespace OCRLessonReport.Imaging
{
    public class TableCell
    {
        public int Column { get; private set; }

        public int Row { get; private set; }

        public TableCellType Type { get; private set; }

        public Bitmap Image { get; private set; }

        public bool Mask { get; private set; }
       
        public string Text { get; private set; } 

        public BitmapImage BitmapImage { get { return Image.ToBitmapImage(); } }

        public TableCell(int column, int row, TableCellType type, Bitmap image, string text, bool mask)
        {
            this.Column = column;
            this.Row = row;
            this.Type = type;
            this.Image = image;
            this.Text = text;
            this.Mask = mask;
        }       
      
    }

    public enum TableCellType
    {
        Text = 0,
        Mark = 1,
        Header = 2,
        HeaderRotated = 3,
        MainHeader = 4
    }
}
