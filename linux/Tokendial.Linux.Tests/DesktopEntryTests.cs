using Tokendial.Linux.Install;

namespace Tokendial.Linux.Tests;

public sealed class DesktopEntryTests
{
    [Fact]
    public void PlainPathIsOnlyQuoted() =>
        Assert.Equal("\"/home/ana/.local/bin/Tokendial.AppImage\"", Desktop.Quote("/home/ana/.local/bin/Tokendial.AppImage"));

    [Fact]
    public void ExecValueEscapesEveryLayerTheSpecificationNames()
    {
        // Verbatim: "" is one quote and every backslash is literal, so this is exactly what the file holds.
        const string expected = @"""/home/ana/my apps/50%% off/\\$HOME/\\""it\\""/\\`x\\`/a\\\\b""";
        Assert.Equal(expected, Desktop.Quote("/home/ana/my apps/50% off/$HOME/\"it\"/`x`/a\\b"));
    }

    [Theory]
    [InlineData("/home/ana/Downloads/Tokendial.AppImage")]
    [InlineData("/home/ana/my apps/Tokendial.AppImage")]
    [InlineData("/home/ana/50% off/$HOME/\"quoted\"/`tick`/back\\slash/Tokendial.AppImage")]
    public void ExecValueReadsBackAsThePath(string path) => Assert.Equal(path, ReadExec(Desktop.Quote(path)));

    [Fact]
    public void TouchMovesTheIconThemeForward()
    {
        var directory = Directory.CreateTempSubdirectory("tokendial-icons-");
        try
        {
            var past = DateTime.UtcNow.AddDays(-2);
            Directory.SetLastWriteTimeUtc(directory.FullName, past);
            Desktop.Touch(directory.FullName);
            Assert.True(Directory.GetLastWriteTimeUtc(directory.FullName) > past.AddDays(1));
            Assert.Empty(directory.EnumerateFileSystemInfos());
        }
        finally { directory.Delete(recursive: true); }
    }

    /// <summary>
    /// A desktop's reading of one quoted Exec argument, layer by layer in the order the specification
    /// gives: the string escapes of the value, then the quoting rule, then the field-code percent.
    /// </summary>
    private static string ReadExec(string value)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                text.Append(value[i] switch { 's' => ' ', 'n' => '\n', 't' => '\t', 'r' => '\r', '\\' => '\\', var other => throw new FormatException($"\\{other}") });
            }
            else text.Append(value[i]);
        }

        var unescaped = text.ToString();
        Assert.True(unescaped.Length >= 2 && unescaped[0] == '"' && unescaped[^1] == '"', unescaped);
        var argument = new System.Text.StringBuilder();
        for (var i = 1; i < unescaped.Length - 1; i++)
        {
            if (unescaped[i] == '\\')
            {
                i++;
                Assert.True("\"`$\\".Contains(unescaped[i]), $"\\{unescaped[i]} is not a quoting escape");
            }
            else Assert.NotEqual('"', unescaped[i]);
            argument.Append(unescaped[i]);
        }

        var result = new System.Text.StringBuilder();
        var raw = argument.ToString();
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '%')
            {
                i++;
                Assert.Equal('%', raw[i]);
            }
            result.Append(raw[i]);
        }
        return result.ToString();
    }
}
