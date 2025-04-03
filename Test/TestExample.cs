using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestExample
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
        L.OpenLibs(); // or L.OpenSafeLibs();
        
        L.PushCsDelegate(FromCS);
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

    private static int FromCS(ILuaState L)
    {
        var a = L.ToInteger(1);
        var b = L.ToInteger(2);
        L.PushInteger(a + 1);
        L.PushInteger(b - 1);
        return 2;
    }
}