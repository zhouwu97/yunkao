using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class AppearancePolicyTests
{
    [Fact]
    public void Preview_reverts_to_the_saved_baseline_until_committed()
    {
        var preview = new AppearancePreviewState();
        preview.Begin("acrylic", "standard");
        preview.Preview("solid", "transparent");

        Assert.True(preview.TryGetRollback(out AppearanceSelection rollback));
        Assert.Equal(new AppearanceSelection("acrylic", "standard"), rollback);
        Assert.False(preview.IsDirty);

        preview.Commit("mica", "clear");
        Assert.False(preview.TryGetRollback(out _));
        Assert.Equal(new AppearanceSelection("mica", "clear"), preview.Saved);
    }

    [Theory]
    [InlineData("acrylic", AppearanceMaterial.DesktopAcrylic)]
    [InlineData("mica", AppearanceMaterial.Mica)]
    [InlineData("solid", AppearanceMaterial.Solid)]
    [InlineData("unsupported", AppearanceMaterial.DesktopAcrylic)]
    [InlineData(null, AppearanceMaterial.DesktopAcrylic)]
    public void Resolves_requested_material_with_acrylic_fallback(
        string? value,
        AppearanceMaterial expected)
    {
        Assert.Equal(expected, AppearancePolicy.ResolveMaterial(value));
    }

    [Theory]
    [InlineData("clear", AppearanceClarity.Clear)]
    [InlineData("standard", AppearanceClarity.Standard)]
    [InlineData("transparent", AppearanceClarity.Transparent)]
    [InlineData("unsupported", AppearanceClarity.Standard)]
    [InlineData(null, AppearanceClarity.Standard)]
    public void Resolves_clarity_with_standard_fallback(
        string? value,
        AppearanceClarity expected)
    {
        Assert.Equal(expected, AppearancePolicy.ResolveClarity(value));
    }
}
