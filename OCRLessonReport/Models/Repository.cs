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

            CreateResultTable(cells);
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
        private void CreateResultTable(List<TableCell> cells)
        {
            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                conn.Open();

                OleDbCommand cmd = conn.CreateCommand();

                var lessonsColumnsStr = String.Join(",", GetLessonsColumns(cells).Select(l => l.Item3));

                if (lessonsColumnsStr.Length > 0)
                    lessonsColumnsStr = "," + lessonsColumnsStr;

                cmd.CommandText = "CREATE TABLE " + ResultTable + "("
                + "id varchar(6) null,"
                + "Class_Name varchar(10) null,"
                + "Class_Date varchar(10) null,"
                + "Student_Number varchar(6) null,"
                + "Student_Name varchar(255) null"              
                + lessonsColumnsStr
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

                var lessonsColumns = GetLessonsColumns(cells);

                var lessonsColumnsStr = String.Join(",", GetLessonsColumns(cells).Select(l => l.Item2));
                if (lessonsColumnsStr.Length > 0)
                    lessonsColumnsStr = "," + lessonsColumnsStr;

                var lessonsColumnsGrouped = lessonsColumns.ToDictionary(c => c.Item1);

                OleDbCommand cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO " + ResultTable + " VALUES (@id, @class, @date, @student_number, @name " + lessonsColumnsStr + ")";

                for (int row = settingsManager.Settings.HeaderStartLine + 1; row < rows.Length; row++)
                {
                    string id = Regex.Replace(rows[row].Cells[0].Text, @"[\s]", "");
                    string student_number = Regex.Replace(rows[row].Cells[1].Text, @"[\s]", "");

                    if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(student_number))
                        continue;

                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@class", className);
                    cmd.Parameters.AddWithValue("@date", date);
                    cmd.Parameters.AddWithValue("@student_number", student_number);
                    cmd.Parameters.AddWithValue("@name", rows[row].Cells[2].Text.Trim());

                    foreach (var cell in rows[row].Cells.Where(c => c.Type == TableCellType.Mark))
                        cmd.Parameters.AddWithValue(lessonsColumnsGrouped[cell.Column].Item2, cell.Mark.ToString());
                 
                    cmd.ExecuteNonQuery();
                    cmd.Parameters.Clear();
                }
            }
        }

        private List<Tuple<int, string, string>> GetLessonsColumns(List<TableCell> cells)
        {
            var lessons = cells.Where(c => (c.Type == (TableCellType.Header | TableCellType.Mark)) && !String.IsNullOrWhiteSpace(c.Text)).OrderBy(c => c.Column).
                            Select((cell, index) => Tuple.Create(cell.Column, String.Format("@lesson_{0}", index + 1), String.Format("Lesson_{0} varchar(5)", index + 1))).
                            ToList();

            return lessons;
        }
    }
}
