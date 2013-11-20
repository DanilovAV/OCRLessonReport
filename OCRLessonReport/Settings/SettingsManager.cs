using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace OCRLessonReport
{
    public class SettingsManager : ISettingsManager
    {
        private const string SettingsFile = "settings.xml";
        private Settings settings;

        public SettingsManager()
        {
        }

        /// <summary>
        /// Settings
        /// </summary>
        public Settings Settings
        {
            get
            {
                if (settings == null)
                    settings = Load();

                return settings;

            }
        }

        /// <summary>
        /// Reset to default settings
        /// </summary>
        public virtual void Reset()
        {
            settings = GetDefaultSettings();
        }

        /// <summary>
        /// Save settings
        /// </summary>
        public virtual void Save()
        {
            IsolatedStorageFile isoStore = null;
            IsolatedStorageFileStream stream = null;
            XmlTextWriter writer = null;

            try
            {
                isoStore = IsolatedStorageFile.GetMachineStoreForDomain();

                if (isoStore.FileExists(SettingsFile))
                {
                    stream = new IsolatedStorageFileStream(SettingsFile,
                        FileMode.Create, isoStore);
                }
                else
                {
                    stream = new IsolatedStorageFileStream(SettingsFile,
                       FileMode.CreateNew, isoStore);
                }

                writer = new XmlTextWriter(stream, Encoding.UTF8);

                writer.Formatting = Formatting.Indented;
                writer.WriteStartDocument();

                XmlSerializer serializer = new XmlSerializer(typeof(Settings));
                serializer.Serialize(writer, Settings);
            }
            finally
            {
                if (writer != null)
                    writer.Flush();

                if (stream != null)
                    stream.Close();

                if (isoStore != null)
                    isoStore.Close();
            }
        }

        /// <summary>
        /// Load settings
        /// </summary>
        /// <returns>settings</returns>
        protected virtual Settings Load()
        {
            IsolatedStorageFile isoStore = null;
            IsolatedStorageFileStream stream = null;        
            Settings settings = null;

            try
            {
                isoStore = IsolatedStorageFile.GetMachineStoreForDomain();

                if (isoStore.FileExists(SettingsFile))
                {
                    stream = new IsolatedStorageFileStream(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read, isoStore);

                    XmlSerializer serializer = new XmlSerializer(typeof(Settings));
                    settings = (Settings)serializer.Deserialize(stream);
                }

                if (settings == null)
                    settings = GetDefaultSettings();
            }
            finally
            {               
                if (stream != null)
                    stream.Close();

                if (isoStore != null)
                    isoStore.Close();
            }

            return settings;
        }

        /// <summary>
        /// Get defaults settings
        /// </summary>
        /// <returns>settings</returns>
        public Settings GetDefaultSettings()
        {
            Settings settings = new Settings();

            settings.VerticalSensitivity = 0.98;
            settings.HorizontalSensitivity = 0.5;
            settings.CellSensitivity = 0.85;
            
            settings.HeaderYOffset = 0.15;                                

            settings.FilteringColor = 130;

            settings.LeftEdgeColorDeviation = 15;
            settings.RightEdgeColorDeviation = 180;

            settings.EdgePixelCheckingLength = 20;

            settings.LeftEdgeOffset = 0.02;
            settings.RightEdgeOffset = 0.02;
            settings.TopEdgeOffset = 0.03;
            settings.BottomEdgeOffset = 0.03;

            settings.LineGroupingDelta = 15;

            settings.HeaderStartLine = 1;
            settings.BottomStartLine = 2;
            settings.NameStartLine = 2;

            settings.ColumnSubjectStart = 3;

            settings.CellXEdgeWith = 0.25;
            settings.CellYEdgeWith = 0.1;
            settings.CellEdgeCutting = 3;
            settings.CellMaskSensitivity = 220;
            settings.CellColorFilter = 200;
            settings.CellMarkDetectRadius = 0.3;

            //Sheet edge detection sensitivity
            settings.SheetEdgeSensitivity = 0.75;
            settings.SheetEdgeVerticalMaxAngle = 5;

            //Tesseract settings
            settings.TessdataLanguage = "tur";
            settings.TessdataPath = @"./tessdata";
            settings.TessdataPrefix = @"./";
            //Increasing brightness
            settings.Brightness = 20;

            return settings;
        }
    }
}
