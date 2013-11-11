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

        public bool Mark { get; private set; }
       
        public string Text { get; private set; } 

        public BitmapImage BitmapImage { get { return Image.ToBitmapImage(); } }

        public TableCell(int column, int row, TableCellType type, Bitmap image, string text, bool mark)
        {
            this.Column = column;
            this.Row = row;
            this.Type = type;
            this.Image = image;
            this.Text = text;
            this.Mark = mark;
        }

        public override string ToString()
        {
            return String.Format("C={0}, R={1}, Value={2}, Type={3}", Column, Row, (Type == TableCellType.Mark) ? Mark.ToString() : Text, Type);
        }
      
    }

    [Flags]
    public enum TableCellType
    {
        Unknown = 0,
        Text = 1,
        Mark = 2,
        Header = 4,     
        MainHeader = 8,        
    }
}
