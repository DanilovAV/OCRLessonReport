using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using ADOX;
using System.Runtime.InteropServices;
using OCRLessonReport.Imaging;
using System.Data.OleDb;
using System.Text.RegularExpressions;

namespace OCRLessonReport.Models
{
    public class Repository
    {
        private static Dictionary<TableCellType, DataTypeEnum> TypeMap = new Dictionary<TableCellType, DataTypeEnum>()
        { 
            { TableCellType.HeaderRotated, DataTypeEnum.adBoolean },
            { TableCellType.Mark, DataTypeEnum.adBoolean },
            { TableCellType.Header, DataTypeEnum.adWChar },
            { TableCellType.Text, DataTypeEnum.adWChar }
        };

        private string connectionString;

        private static string ResultTable = "Result";
        private ISettingsManager settingsManager;

        public Repository(ISettingsManager settingsManager)
        {
            this.settingsManager = settingsManager;
        }

        public void CreateAndExportLegacyFile(string exportFilePath, List<TableCell> cells)
        {
            if (File.Exists(exportFilePath))
                File.Delete(exportFilePath);

            connectionString = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + exportFilePath + ";Jet OLEDB:Engine Type=5";

            var catalog = new Catalog();
            catalog.Create(connectionString);
                      
            Marshal.FinalReleaseComObject(catalog.ActiveConnection);
            Marshal.FinalReleaseComObject(catalog);

            var headerInfo = GetHeaderInfo(cells);

            CreateResultTable();
            InsertIntoResultTable(cells, headerInfo.Item1, headerInfo.Item2);   
        }

        /// <summary>
        /// Get info from header - class and date (Item1 - name of class, Item2 - date)
        /// </summary>
        /// <param name="cells">Cells</param>
        /// <returns>Item1 - name of class, Item2 - date</returns>
        private Tuple<string, string> GetHeaderInfo(List<TableCell> cells)
        {
            var topHeader = cells.FirstOrDefault(c => c.Type == TableCellType.MainHeader);

            if (topHeader == null)
                throw new Exception("Impossible to recognize top header data.");
            
            //Example text for tests
            //var text = "INĞİRLI Tısîmîdîêéîoüârâl MESLEK LİSESİ ”\nÖĞRENCİ GÜNLÜK YOKLAMA FİŞİ\nC 11 /A)slNIFI LİSTESİ ( 3o,1o.2o13)çARsAMaA\n\n";

            var splitted = topHeader.Text.Trim().Split(new string[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);

            string className = String.Empty;
            string date = String.Empty;

            if (splitted.Length > 0)
            {
                var str = splitted.LastOrDefault();
                str = Regex.Replace(str, @"[\s]", "").Replace(",", ".").Replace("o", "0");

                //use regex, because text can be not clear
                var match = Regex.Match(str, @"(\d{1,2}).(.)");

                if (match.Success && match.Groups.Count > 2)                
                    className = match.Groups[1].Value + " / " + match.Groups[2].Value;

                match = Regex.Match(str, @"(\d{2}\.\d{2}.\d{4})");

                if (match.Success)
                    date = match.Value;
            }

            Tuple<string, string> result = new Tuple<string, string>(className, date);

            return result;      
        }

        /// <summary>
        /// Create result table
        /// </summary>
        private void CreateResultTable()
        {
            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                conn.Open();

                OleDbCommand cmd = conn.CreateCommand();

                cmd.CommandText = "CREATE TABLE " + ResultTable + "("
                + "ID int PRIMARY KEY,"
                + "Name varchar(255),"
                + "Lesson bit,"
                + "Class varchar(10),"
                + "Data varchar(10)"
                + ")";

                cmd.ExecuteNonQuery();
           }                       
        }

        private void InsertIntoResultTable(List<TableCell> cells, string className, string date)
        {
            var rows = cells.GroupBy(c => c.Row).Select(p => new { Row = p.Key, Cells = p.ToArray() }).ToArray();

            if (rows.Length < settingsManager.Settings.HeaderStartLine + 1)
                return;
         
            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                conn.Open();

                OleDbCommand cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO " + ResultTable + " VALUES (@id, @name, @lesson, @class, @data)";

                for (int row = settingsManager.Settings.HeaderStartLine + 1; row < rows.Length; row++)
                {                    
                    int id;

                    if (!Int32.TryParse(rows[row].Cells[1].Text.Trim(), out id))
                        continue;

                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@name", rows[row].Cells[2].Text.Trim());
                    cmd.Parameters.AddWithValue("@lesson", rows[row].Cells[3].Mask);
                    cmd.Parameters.AddWithValue("@class", className);
                    cmd.Parameters.AddWithValue("@data", date);
                    cmd.ExecuteNonQuery();
                    cmd.Parameters.Clear();
                }
            }              
        }
    }
}
