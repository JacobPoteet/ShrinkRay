using ShrinkRay.Core.Encode;

namespace ShrinkRay.Core.Tests.Encode;

public class HandBrakeProgressParserTests
{
    [Fact]
    public void Parses_a_full_progress_line()
    {
        Assert.True(HandBrakeProgressParser.TryParse(
            "Encoding: task 1 of 1, 41.23 % (312.45 fps, avg 298.10 fps, ETA 00h01m10s)", out var p));

        Assert.Equal(1, p.Task);
        Assert.Equal(1, p.TaskCount);
        Assert.Equal(41.23, p.Percent);
        Assert.Equal(312.45, p.Fps);
        Assert.Equal(298.10, p.AverageFps);
        Assert.Equal(TimeSpan.FromSeconds(70), p.Eta);
    }

    [Fact]
    public void Parses_a_line_without_fps_or_eta()
    {
        Assert.True(HandBrakeProgressParser.TryParse("Encoding: task 1 of 1, 0.52 %", out var p));

        Assert.Equal(0.52, p.Percent);
        Assert.Null(p.Fps);
        Assert.Null(p.AverageFps);
        Assert.Null(p.Eta);
    }

    [Fact]
    public void Parses_a_line_with_fps_but_no_eta()
    {
        Assert.True(HandBrakeProgressParser.TryParse("Encoding: task 2 of 2, 99.00 % (120.00 fps", out var p));

        Assert.Equal(2, p.Task);
        Assert.Equal(120.0, p.Fps);
        Assert.Null(p.Eta);
    }

    [Fact]
    public void Parses_long_etas()
    {
        Assert.True(HandBrakeProgressParser.TryParse(
            "Encoding: task 1 of 1, 1.00 % (12.00 fps, avg 11.50 fps, ETA 26h05m09s)", out var p));

        Assert.Equal(new TimeSpan(26, 5, 9), p.Eta);
    }

    [Fact]
    public void Ignores_surrounding_text_and_carriage_returns()
    {
        Assert.True(HandBrakeProgressParser.TryParse(
            "\rEncoding: task 1 of 1, 50.00 % (200.00 fps, avg 190.00 fps, ETA 00h00m30s)\r", out var p));

        Assert.Equal(50.0, p.Percent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Muxing: this may take awhile...")]
    [InlineData("[00:12:01] Starting work at: Mon Nov 24 00:12:01 2026")]
    [InlineData("Encoding: task 1 of 1")]
    public void Rejects_other_lines(string? line)
    {
        Assert.False(HandBrakeProgressParser.TryParse(line, out _));
    }
}
