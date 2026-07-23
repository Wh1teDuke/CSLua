using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestComment
{
    [Fact]
    public void TestSingle1()
    {
        var L = Lua.New();
        L.Eval("""
               -- Foo
               local a = 1 -- Foo
               -- Bar
               local b = 2 -- Bar
               -- Baz
               return a + b
               """);
        var res = L.PopInteger();
        Assert.Equal(3, res);
    }
    
    [Fact]
    public void TestMulti1()
    {
        var L = Lua.New();
        L.Eval("""
               --[[ Foo
               ]]
               local a = 1 --[[ Foo
               -- Bar]]
               local b = 2 --[[ Bar
               -- Baz]]
               return a + b
               """);
        var res = L.PopInteger();
        Assert.Equal(3, res);
    }
}