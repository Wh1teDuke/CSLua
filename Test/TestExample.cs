using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestExample
{
    [Fact]
    public void Test1()
    {
        var L = Lua.New();
        L.OpenLibs(); // or L.OpenSafeLibs();
        
        L.PushCsDelegate(FromCS1);
        L.SetGlobal("FromCS");
        
        L.DoString(
            """
            assert(_CSLUA)
            local a, b = 1, 2
            return FromCS(a, b);
            """);

        var b = L.PopInteger();
        var a = L.PopInteger();
        Assert.Equal(2, a);
        Assert.Equal(1, b);
    }

    private static int FromCS1(LuaState L)
    {
        var a = L.ToInteger(1);
        var b = L.ToInteger(2);
        Assert.Equal(1, a);
        Assert.Equal(2, b);
        L.PushInteger(a + 1);
        L.PushInteger(b - 1);
        return 2;
    }
    
    [Fact]
    public void Test2()
    {
        var L = Lua.New();
        L.OpenLibs(); // or L.OpenSafeLibs();
        L.SetGlobal("AddFromCS", AddFromCs);
        
        L.DoString(
            """
            assert(_CSLUA)
            local a, b = 1, 2
            return AddFromCS(a, b);
            """);

        var r = L.PopInteger();
        Assert.Equal(3, r);
    }

    private static int AddFromCs(LuaState L)
    {
        var a = L.ToInteger(1);
        var b = L.ToInteger(2);
        Assert.Equal(1, a);
        Assert.Equal(2, b);
        L.PushInteger(a + b);
        return 1;
    }
}