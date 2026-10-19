using System.Security.Cryptography;
using System.Text;

namespace StemForge.Core.Cache;

/// <summary>
/// 智慧分軌節點快取服務：
/// 對原始音訊計算 SHA-256 指紋，每個分離樹節點（包含中繼母軌如 V0_Vocals_All, D0_Drums_All 及終端葉節點）
/// 都會以 {SongHash}_{NodeId}_{EngineVersion}.wav 儲存在 workspace_data/cache/。
/// 若使用者後續補勾選「鼓組 6 軌」或「吉他細分」，系統可直接命中母軌快取，0 秒跳過前置重型模型！
/// </summary>
public sealed class StemNodeCacheService
{
    private readonly string _cacheRootDir;

    public StemNodeCacheService(string workspaceDataRoot)
    {
        _cacheRootDir = Path.Combine(workspaceDataRoot, "cache");
        Directory.CreateDirectory(_cacheRootDir);
    }

    public async Task<string> ComputeFileSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hashBytes)[..20];
    }

    public string GetCacheFilePath(string songHash, string nodeId, string modelTag = "v1")
    {
        var safeNode = SanitizeFileName(nodeId);
        var safeTag = SanitizeFileName(modelTag);
        var songDir = Path.Combine(_cacheRootDir, songHash);
        Directory.CreateDirectory(songDir);
        return Path.Combine(songDir, $"{safeNode}__{safeTag}.wav");
    }

    public bool TryGetCachedStem(string songHash, string nodeId, out string cachedWavPath, string modelTag = "v1")
    {
        cachedWavPath = GetCacheFilePath(songHash, nodeId, modelTag);
        if (File.Exists(cachedWavPath))
        {
            var info = new FileInfo(cachedWavPath);
            if (info.Length > 4096) // 確保不是空的損毀檔
            {
                return true;
            }
        }
        return false;
    }

    public void StoreStemInCache(string sourceWavPath, string songHash, string nodeId, string modelTag = "v1")
    {
        if (!File.Exists(sourceWavPath)) return;
        var target = GetCacheFilePath(songHash, nodeId, modelTag);
        if (!string.Equals(Path.GetFullPath(sourceWavPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceWavPath, target, overwrite: true);
        }
    }

    private static string SanitizeFileName(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
        }
        return sb.ToString();
    }
}
