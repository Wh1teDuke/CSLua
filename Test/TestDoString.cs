using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestDoString
{
    [Fact]
    public void Test1()
    {
        var L = Lua.New();
        L.OpenLibs();
        L.DoString(
            """
            function foo(bar) print(bar) end
            assert(_CSLUA)
            local bar = "bar"
            foo(bar)
            return 7;
            """);

        Assert.Equal(7, L.PopInteger());
    }
}