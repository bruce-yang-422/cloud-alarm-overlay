using System.Windows.Controls;
using CloudAlarmOverlay.App.Controls;

namespace CloudAlarmOverlay.App.Tests;

public sealed class CyclicTimeSelectorTests
{
    [Theory]
    [InlineData(59,"45",true,45,"45")]
    [InlineData(59,"0",true,0,"00")]
    [InlineData(59,"59",true,59,"59")]
    [InlineData(23,"23",true,23,"23")]
    [InlineData(59,"60",false,12,"60")]
    [InlineData(23,"24",false,12,"24")]
    [InlineData(59,"",false,12,"")]
    [InlineData(59,"ab",false,12,"ab")]
    [InlineData(59,"-1",false,12,"-1")]
    public async Task Direct_input_commits_valid_numbers_and_rejects_invalid_values(int maximum,string input,bool valid,int expected,string display)
    {
        await MilestoneOneTests.RunSta(()=>
        {
            var selector=new CyclicTimeSelector{Maximum=maximum,Value=12};
            var editor=(TextBox)selector.FindName("ValueEditor");
            Assert.Equal("12",editor.Text);
            editor.Text=input;
            Assert.Equal(input,editor.Text);
            Assert.Equal(valid,selector.TryCommitInput());
            Assert.Equal(expected,selector.Value);
            Assert.Equal(display,editor.Text);
            selector.Value=7;
            Assert.Equal("07",editor.Text);
            return Task.CompletedTask;
        });
    }
}
