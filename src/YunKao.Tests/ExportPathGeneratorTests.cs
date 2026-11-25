using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class ExportPathGeneratorTests
{
    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("题库:章节?*")]
    public void Prefix_is_a_safe_filename_inside_the_selected_directory(string prefix)
    {
        string directory = Path.Combine(Path.GetTempPath(), "yunkao-prefix-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = ExportPathGenerator.CreateUniquePath(directory, prefix, "txt");
            Assert.Equal(Path.GetFullPath(directory), Path.GetDirectoryName(Path.GetFullPath(path)));
            Assert.DoesNotContain(Path.GetFileName(path), c => Path.GetInvalidFileNameChars().Contains(c));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CreateUniquePath_generates_clean_path_without_guid()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "yunkao_test_export_" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = ExportPathGenerator.CreateUniquePath(tempDir, "高等数学", "docx");
            string fileName = Path.GetFileName(path);

            Assert.StartsWith("高等数学_", fileName);
            Assert.EndsWith(".docx", fileName);
            Assert.DoesNotContain("-", fileName); // No GUID hyphens
            Assert.False(fileName.Length > 40); // Clean timestamp length
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CreateUniquePath_resolves_duplicate_filename_with_increment_suffix()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "yunkao_test_export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var timestamp = new DateTime(2026, 11, 25, 10, 0, 0);
            string path1 = ExportPathGenerator.CreateUniquePath(tempDir, "测试题库", "pdf", timestamp);
            File.WriteAllText(path1, "dummy");

            string path2 = ExportPathGenerator.CreateUniquePath(tempDir, "测试题库", "pdf", timestamp);
            File.WriteAllText(path2, "dummy");

            string path3 = ExportPathGenerator.CreateUniquePath(tempDir, "测试题库", "pdf", timestamp);

            Assert.NotEqual(path1, path2);
            Assert.NotEqual(path2, path3);
            Assert.EndsWith("_2.pdf", path2);
            Assert.EndsWith("_3.pdf", path3);
            string nextSecond = ExportPathGenerator.CreateUniquePath(tempDir, "测试题库", "pdf", timestamp.AddSeconds(1));
            Assert.Equal("测试题库_20261125_100001.pdf", Path.GetFileName(nextSecond));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("markdown", "md")]
    [InlineData("MD", "md")]
    [InlineData(".docx", "docx")]
    [InlineData("txt", "txt")]
    [InlineData("pdf", "pdf")]
    [InlineData("unknown", "pdf")]
    public void NormalizeExtension_returns_standard_extension(string input, string expected)
    {
        Assert.Equal(expected, ExportPathGenerator.NormalizeExtension(input));
    }
}
