using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class WorkspaceLayoutTests
{
    [Theory]
    [InlineData(899.99, WorkspaceLayoutMode.Narrow)]
    [InlineData(900, WorkspaceLayoutMode.Medium)]
    [InlineData(1179.99, WorkspaceLayoutMode.Medium)]
    [InlineData(1180, WorkspaceLayoutMode.Wide)]
    public void Uses_inclusive_900_and_1180_breakpoints(double width, WorkspaceLayoutMode expected)
    {
        Assert.Equal(expected, WorkspaceLayoutBreakpoints.GetMode(width));
    }
}
