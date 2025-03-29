using CSLua.Parse;

namespace Test;

public sealed class TestParseNumber
{
    [Fact]
    public void Test1()
    {
        Assert.Equal(0, Int("0"));
        Assert.Equal(123456, Int("123456"));
        Assert.Equal(0x1A, Int("0x1A"));
        Assert.Equal(0x1ABCDEF1, Int("0x1ABCDEF1"));
    }
    
    private static int Int(string src)
    {
        var parser = Of(src);
        // Const#1   string    "n1"
        // Const#2   integer   ???
        return (int)parser.Proto.K[1].NValue;
    }
    
    private static Parser Of(string src) => 
        Parser.Read("n1 = " + src, "Test");
}