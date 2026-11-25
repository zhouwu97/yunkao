namespace YunKao.Core.Services;

/// <summary>
/// 统一导出文件路径生成器：规范化前缀与时间戳，重名时自增序号（_2, _3），杜绝无意义的随机 GUID。
/// </summary>
public static class ExportPathGenerator
{
    public static string CreateUniquePath(string directory, string? prefix, string format, DateTime? timestamp = null)
    {
        string targetDir = string.IsNullOrWhiteSpace(directory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : directory.Trim();
        Directory.CreateDirectory(targetDir);

        string ext = NormalizeExtension(format);
        string cleanPrefix = string.IsNullOrWhiteSpace(prefix) ? "云考题库导出" : prefix.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        cleanPrefix = new string(cleanPrefix.Select(character => invalid.Contains(character)
            || character is '/' or '\\' ? '_' : character).ToArray()).Trim(' ', '.');
        if (cleanPrefix.Length == 0) cleanPrefix = "云考题库导出";
        if (cleanPrefix.Length > 80) cleanPrefix = cleanPrefix[..80];
        string baseName = $"{cleanPrefix}_{timestamp ?? DateTime.Now:yyyyMMdd_HHmmss}";

        string candidate = Path.Combine(targetDir, $"{baseName}.{ext}");
        if (!File.Exists(candidate)) return candidate;

        for (int index = 2; index <= 9999; index++)
        {
            string indexedCandidate = Path.Combine(targetDir, $"{baseName}_{index}.{ext}");
            if (!File.Exists(indexedCandidate)) return indexedCandidate;
        }

        return Path.Combine(targetDir, $"{baseName}_{DateTime.Now.Ticks}.{ext}");
    }

    public static string NormalizeExtension(string format)
    {
        string normalized = (format ?? "").Trim().TrimStart('.').ToLowerInvariant();
        return normalized switch
        {
            "markdown" or "md" => "md",
            "docx" => "docx",
            "txt" => "txt",
            "pdf" or _ => "pdf",
        };
    }
}
