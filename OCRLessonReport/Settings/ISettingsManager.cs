using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OCRLessonReport
{
    public interface ISettingsManager
    {
        /// <summary>
        /// Settings
        /// </summary>
        Settings Settings { get; }

        /// <summary>
        /// Reset to default settings
        /// </summary>
        void Reset();

        /// <summary>
        /// Save settings
        /// </summary>
        void Save();

        /// <summary>
        /// Get defaults settings
        /// </summary>
        /// <returns>settings</returns>
        Settings GetDefaultSettings();
    }
}
