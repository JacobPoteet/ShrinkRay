using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Setup;

namespace ShrinkRay.Core.Tests.Setup;

public class FirstRunFlowTests
{
    private static readonly FirstRunFacts Nothing = new(false, false, false, null);
    private static readonly FirstRunFacts Everything = new(true, true, true, true);

    private static FirstRunFlow At(FirstRunStep step)
    {
        var flow = new FirstRunFlow();
        flow.Begin();
        while (flow.Step != step)
        {
            Assert.True(flow.Next(Everything));
        }

        return flow;
    }

    [Theory]
    [InlineData(FirstRunStep.Tools)]
    [InlineData(FirstRunStep.Library)]
    [InlineData(FirstRunStep.Scratch)]
    [InlineData(FirstRunStep.Originals)]
    public void A_card_stays_until_it_has_what_it_needs(FirstRunStep step)
    {
        var flow = At(step);

        Assert.False(flow.Next(Nothing));

        Assert.Equal(step, flow.Step);
    }

    [Fact]
    public void Each_card_needs_only_its_own_fact()
    {
        Assert.True(FirstRunFlow.CanAdvance(FirstRunStep.Tools, Nothing with { ToolsFound = true }));
        Assert.True(FirstRunFlow.CanAdvance(FirstRunStep.Library, Nothing with { LibraryHasEpisodes = true }));
        Assert.True(FirstRunFlow.CanAdvance(FirstRunStep.Scratch, Nothing with { ScratchUsable = true }));
        Assert.True(FirstRunFlow.CanAdvance(FirstRunStep.Originals, Nothing with { ReplaceOriginals = false }));
        Assert.False(FirstRunFlow.CanAdvance(FirstRunStep.Off, Everything));
    }

    [Fact]
    public void The_steps_run_in_order_and_end_off()
    {
        var flow = new FirstRunFlow();
        flow.Begin();
        var seen = new List<FirstRunStep> { flow.Step };
        while (flow.Next(Everything))
        {
            seen.Add(flow.Step);
        }

        Assert.Equal(
            [
                FirstRunStep.Tools, FirstRunStep.Library, FirstRunStep.Scratch, FirstRunStep.Originals,
                FirstRunStep.TourLibrary, FirstRunStep.TourEncode, FirstRunStep.TourSavings, FirstRunStep.Off,
            ],
            seen);
    }

    [Theory]
    [InlineData(FirstRunStep.Tools, false)]
    [InlineData(FirstRunStep.Library, false)]
    [InlineData(FirstRunStep.Scratch, true)]
    [InlineData(FirstRunStep.Originals, true)]
    [InlineData(FirstRunStep.TourLibrary, false)]
    public void Back_works_after_the_first_question_only(FirstRunStep step, bool goesBack)
    {
        var flow = At(step);

        Assert.Equal(goesBack, flow.Back());

        Assert.Equal(goesBack ? step - 1 : step, flow.Step);
    }

    [Theory]
    [InlineData(FirstRunStep.TourLibrary, true)]
    [InlineData(FirstRunStep.TourSavings, true)]
    [InlineData(FirstRunStep.Originals, false)]
    [InlineData(FirstRunStep.Tools, false)]
    public void Skip_ends_the_tour_and_never_a_card(FirstRunStep step, bool skips)
    {
        var flow = At(step);

        Assert.Equal(skips, flow.Skip());

        Assert.Equal(skips ? FirstRunStep.Off : step, flow.Step);
    }

    [Fact]
    public void The_tour_starts_alone_and_Cancel_closes_anything()
    {
        var flow = new FirstRunFlow();
        flow.BeginTour();
        Assert.True(flow.IsTour);
        Assert.False(flow.IsCard);

        flow.Cancel();

        Assert.False(flow.IsRunning);
    }

    [Theory]
    [InlineData(null, false, "", true)]
    [InlineData(null, false, @"X:\TV", false)]
    [InlineData(false, false, @"X:\TV", true)]
    [InlineData(false, true, @"X:\TV", false)]
    [InlineData(true, false, "", false)]
    public void Setup_runs_for_a_fresh_or_unfinished_install(bool? completed, bool pending, string library, bool needed)
    {
        var settings = new ShrinkRaySettings
        {
            FirstRunCompleted = completed,
            FirstEpisodePending = pending,
            LibraryRoot = library,
        };

        Assert.Equal(needed, FirstRunFlow.NeedsSetup(settings));
        Assert.Equal(completed == false && pending, FirstRunFlow.IsFirstEpisodePending(settings));
    }
}
