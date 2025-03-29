using CSLua.Parse;

namespace Test;

public sealed class TestParseString
{
    [Fact]
    public void Test1()
    {
        Assert.Equal("bar", Str("\"bar\""));
        Assert.Equal("bar", Str("'bar'"));
        Assert.Equal("bar", Str("[[bar]]"));
        Assert.Equal("foobar", Str("[[foobar]]"));
        Assert.Equal("foobar", Str("  [[foobar]]"));
        Assert.Equal("foobar", Str("  [[foobar]]  "));
        Assert.Equal("foobar  ", Str("[[foobar  ]]"));
        Assert.Equal("  foobar", Str("[[  foobar]]"));
        Assert.Equal("Hello World!", Str(@"'Hello\x20World\x21'"));
    }

    [Fact]
    public void Test2()
    {
        Valid("_ = {1 or a}");
        Valid("_ = (a or 1)");
        Valid("_ = {a or 1}");
    }

    private static string Str(string src)
    {
        var parser = Of(src);
        // Const#1   string   "s1"
        // Const#2   string   ???
        return parser.Proto.K[1].SValue();
    }
    
    private static Parser Of(string src) => 
        Parser.Read("s1 = " + src, "Test");

    private static void Valid(string src) => Parser.Read(src, "Test");
}