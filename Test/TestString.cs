using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestString
{
    [Fact]
    public void TestConcat()
    {
        var L = new LuaState();
        L.Eval("return 'foo' .. 'bar'");
        var foobar = L.PopString();
        Assert.Equal("foobar", foobar);
    }
}