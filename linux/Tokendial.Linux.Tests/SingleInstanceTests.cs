namespace Tokendial.Linux.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void RuntimeDirectoryIsUsedAsItIs() =>
        Assert.Equal(Path.Combine("/run/user/1000", "tokendial.lock"),
            SingleInstance.LockPath("/run/user/1000", "/tmp", "ana"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/dir")]
    public void SharedTemporaryDirectoryGivesEveryUserTheirOwnLock(string? runtime)
    {
        var ana = SingleInstance.LockPath(runtime, "/tmp", "ana");
        var luis = SingleInstance.LockPath(runtime, "/tmp", "luis");

        Assert.Equal(Path.Combine("/tmp", "tokendial-ana.lock"), ana);
        Assert.NotEqual(ana, luis);
    }
}
