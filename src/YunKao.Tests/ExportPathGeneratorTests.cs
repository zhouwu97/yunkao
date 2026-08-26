using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class ExportPathGeneratorTests
{
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
            string path1 = ExportPathGenerator.CreateUniquePath(tempDir, "测试题库", "pdf");
            File.WriteAllText(path1, "dummy");

            string path2 = ExportPathGenerator.CreateUniquePath(tempDir, "测试题库", "pdf");
            File.WriteAllText(path2, "dummy");

            string path3 = ExportPathGenerator.CreateUniquePath(tempDir, "测试题库", "pdf");

            Assert.NotEqual(path1, path2);
            Assert.NotEqual(path2, path3);
            Assert.EndsWith("_2.pdf", path2);
            Assert.EndsWith("_3.pdf", path3);
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
