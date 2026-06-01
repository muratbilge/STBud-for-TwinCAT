using STFormatter.Core.Text;

namespace STFormatter.Core.Tests
{
    public class LineCounterTests
    {
        [Fact]
        public void Null_Or_Empty_Returns_Zero()
        {
            Assert.Equal(0, LineCounter.Count(null));
            Assert.Equal(0, LineCounter.Count(""));
        }

        [Fact]
        public void Crlf_Treated_As_Single_Separator()
        {
            Assert.Equal(2, LineCounter.Count("a\r\nb"));
            Assert.Equal(3, LineCounter.Count("a\r\nb\r\n"));
        }

        [Fact]
        public void Lf_Treated_As_Separator()
        {
            Assert.Equal(2, LineCounter.Count("a\nb"));
            Assert.Equal(3, LineCounter.Count("a\nb\nc"));
        }

        [Fact]
        public void Cr_Treated_As_Separator()
        {
            Assert.Equal(2, LineCounter.Count("a\rb"));
        }

        [Fact]
        public void Mixed_Separators_Handled()
        {
            Assert.Equal(4, LineCounter.Count("a\r\nb\nc\rd"));
        }

        [Fact]
        public void Single_Line_No_Separator_Returns_One()
        {
            Assert.Equal(1, LineCounter.Count("hello world"));
        }
    }
}
