using System.IO.Compression;
using StemForge.Core.Contracts;

namespace StemForge.Export;

/// <summary>
/// 一鍵將所有分軌 WAV、已產生的 MIDI 檔與 Reaper (.rpp) 工程檔打包為結構化 ZIP 壓縮檔
/// </summary>
public static class StemZipArchiver
{
    public static string BuildJobZipArchive(
        string outputZipPath,
        SeparationJobResult jobResult,
        string? reaperRppPath,
        IEnumerable<string>? selectedNodeIds = null)
    {
        var dir = Path.GetDirectoryName(outputZipPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(outputZipPath))
            File.Delete(outputZipPath);

        using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);

        if (!string.IsNullOrEmpty(reaperRppPath) && File.Exists(reaperRppPath))
        {
            zip.CreateEntryFromFile(reaperRppPath, Path.GetFileName(reaperRppPath), CompressionLevel.Fastest);
        }

        var nodeSet = selectedNodeIds != null ? new HashSet<string>(selectedNodeIds, StringComparer.OrdinalIgnoreCase) : null;

        foreach (var stem in jobResult.Stems)
        {
            if (nodeSet != null && !nodeSet.Contains(stem.NodeId))
                continue;

            if (File.Exists(stem.FilePath))
            {
                zip.CreateEntryFromFile(stem.FilePath, Path.GetFileName(stem.FilePath), CompressionLevel.Fastest);
            }
            if (!string.IsNullOrEmpty(stem.MidiFilePath) && File.Exists(stem.MidiFilePath))
            {
                zip.CreateEntryFromFile(stem.MidiFilePath, $"MIDI/{Path.GetFileName(stem.MidiFilePath)}", CompressionLevel.Optimal);
            }
        }

        return outputZipPath;
    }
}
