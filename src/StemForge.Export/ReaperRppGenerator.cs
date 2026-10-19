using System.Globalization;
using System.Text;
using StemForge.Core.Contracts;

namespace StemForge.Export;

/// <summary>
/// 一鍵產生 Reaper (.rpp) 分軌群組工程檔：
/// 自動依照 GroupCategory 建立 Folder Bus 群組軌 (ISBUS 1 1 / ISBUS 2 -1)，
/// 自動套用軌道專屬配色並掛載所有分離出的 32-bit Float WAV 音訊片段。
/// </summary>
public static class ReaperRppGenerator
{
    public static string GenerateReaperProjectFile(
        string outputRppPath,
        string projectTitle,
        int sampleRate,
        double durationSeconds,
        IReadOnlyList<StemTrackResult> stems)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        string rppDir = Path.GetDirectoryName(outputRppPath) ?? "";

        sb.AppendLine("<REAPER_PROJECT 0.1 \"7.0\" 1729300000");
        sb.AppendLine($"  SAMPLERATE {sampleRate} 0 0");
        sb.AppendLine("  TEMPO 120 4 4");
        sb.AppendLine($"  <NOTES 0 2");
        sb.AppendLine($"    |StemForge AI Fine-Grained Separation Project: {projectTitle}");
        sb.AppendLine($"    |Stems Count: {stems.Count} | Sample Rate: {sampleRate} Hz");
        sb.AppendLine("  >");

        var grouped = stems
            .GroupBy(s => s.GroupCategory)
            .ToList();

        foreach (var group in grouped)
        {
            var groupStems = group.ToList();
            if (groupStems.Count == 0) continue;

            int folderColor = HexToReaperNativeColor(groupStems[0].ColorHex);

            // 1. 建立 Folder Bus 父軌道 (ISBUS 1 1)
            sb.AppendLine("  <TRACK");
            sb.AppendLine($"    NAME \"[BUS] {group.Key}\"");
            sb.AppendLine($"    PEAKCOL {folderColor}");
            sb.AppendLine("    VOLPAN 1 0 -1 -1 1");
            sb.AppendLine("    MUTESOLO 0 0 0");
            sb.AppendLine("    ISBUS 1 1");
            sb.AppendLine("  >");

            // 2. 建立群組內的所有子音軌
            for (int i = 0; i < groupStems.Count; i++)
            {
                var stem = groupStems[i];
                bool isLastInFolder = (i == groupStems.Count - 1);
                int trackColor = HexToReaperNativeColor(stem.ColorHex);

                string relFile = Path.GetRelativePath(rppDir, stem.FilePath);

                sb.AppendLine("  <TRACK");
                sb.AppendLine($"    NAME \"{stem.DisplayName}\"");
                sb.AppendLine($"    PEAKCOL {trackColor}");
                sb.AppendLine("    VOLPAN 1 0 -1 -1 1");
                sb.AppendLine("    MUTESOLO 0 0 0");
                sb.AppendLine(isLastInFolder ? "    ISBUS 2 -1" : "    ISBUS 0 0");
                sb.AppendLine("    <ITEM");
                sb.AppendLine("      POSITION 0");
                sb.AppendLine($"      LENGTH {durationSeconds.ToString("F6", inv)}");
                sb.AppendLine("      FADEIN 1 0 0 1 0 0 0");
                sb.AppendLine("      FADEOUT 1 0 0 1 0 0 0");
                sb.AppendLine($"      NAME \"{Path.GetFileName(stem.FilePath)}\"");
                sb.AppendLine("      VOLPAN 1 0 1 -1");
                sb.AppendLine("      <SOURCE WAVE");
                sb.AppendLine($"        FILE \"{relFile}\"");
                sb.AppendLine("      >");
                sb.AppendLine("    >");
                sb.AppendLine("  >");
            }
        }

        sb.AppendLine(">");

        Directory.CreateDirectory(rppDir);
        File.WriteAllText(outputRppPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return outputRppPath;
    }

    private static int HexToReaperNativeColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return 16777216;
        int r = Convert.ToInt32(hex[..2], 16);
        int g = Convert.ToInt32(hex[2..4], 16);
        int b = Convert.ToInt32(hex[4..6], 16);
        // Reaper native color format: (B << 16) | (G << 8) | R | 0x1000000
        return (b << 16) | (g << 8) | r | 0x01000000;
    }
}
