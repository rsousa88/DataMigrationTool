// System
using System;
using System.IO;

// Newtonsoft
using Newtonsoft.Json;

namespace Dataverse.XrmTools.DataMigrationTool.Tests.TestSupport
{
    public sealed class TemporaryFileScope : IDisposable
    {
        public TemporaryFileScope()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "DmtTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string GetPath(string fileName)
        {
            return Path.Combine(DirectoryPath, fileName);
        }

        public string WriteJson<T>(string fileName, T value)
        {
            var path = GetPath(fileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
            return path;
        }

        public string WriteText(string fileName, string content = "")
        {
            var path = GetPath(fileName);
            File.WriteAllText(path, content ?? string.Empty);
            return path;
        }

        public string GetExcelPath(string fileName = "workbook.xlsx")
        {
            return GetPath(fileName);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, true);
        }
    }
}
