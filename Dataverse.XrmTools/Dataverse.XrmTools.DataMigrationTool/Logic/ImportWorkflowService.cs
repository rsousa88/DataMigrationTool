// System
using System;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public enum LargeImportWarningLevel
    {
        None,
        Information,
        Warning
    }

    public class LargeImportWarning
    {
        public LargeImportWarningLevel Level { get; set; }
        public string Message { get; set; }
        public bool ShouldConfirm => Level != LargeImportWarningLevel.None;
    }

    public static class ImportWorkflowService
    {
        public const int LargeExcelImportRowWarning = 1000;
        public const int VeryLargeExcelImportRowWarning = 5000;
        public const int HugeExcelImportRowWarning = 20000;

        public static LargeImportWarning GetLargeExcelImportWarning(int rowCount)
        {
            if (rowCount < LargeExcelImportRowWarning)
                return new LargeImportWarning { Level = LargeImportWarningLevel.None };

            if (rowCount >= HugeExcelImportRowWarning)
            {
                return new LargeImportWarning
                {
                    Level = LargeImportWarningLevel.Warning,
                    Message = $"This Excel file contains {rowCount:N0} data rows.{Environment.NewLine}{Environment.NewLine}"
                        + "This is a very large import and can take a long time to preview and import. Consider splitting the file or using JSON import for this volume."
                        + $"{Environment.NewLine}{Environment.NewLine}Continue?"
                };
            }

            if (rowCount >= VeryLargeExcelImportRowWarning)
            {
                return new LargeImportWarning
                {
                    Level = LargeImportWarningLevel.Warning,
                    Message = $"This Excel file contains {rowCount:N0} data rows.{Environment.NewLine}{Environment.NewLine}"
                        + "Preview and import may take a while, especially when lookup or match-key resolution is enabled. A smaller batch size is recommended for tables with plugins."
                        + $"{Environment.NewLine}{Environment.NewLine}Continue?"
                };
            }

            return new LargeImportWarning
            {
                Level = LargeImportWarningLevel.Information,
                Message = $"This Excel file contains {rowCount:N0} data rows.{Environment.NewLine}{Environment.NewLine}"
                    + "Preview and import may take some time."
                    + $"{Environment.NewLine}{Environment.NewLine}Continue?"
            };
        }
    }
}
