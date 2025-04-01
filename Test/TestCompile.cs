using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestCompile
{
    [Fact]
    public void TestCompile1()
    {
        var L = new LuaState();
        var r1 = L.L_DoString("foo = 0");
        Assert.Equal(ThreadStatus.LUA_OK, r1);
        Assert.Equal(0, L.GetTop());
    
        var r2 = L.Compile("s1","foo = foo + 1");
        Assert.Equal(ThreadStatus.LUA_OK, r2);
        Assert.Equal(0, L.GetTop());

        for (var i = 0; i < 10; i++)
        {
            L.GetGlobal("s1");
            L.Call(0, 0);
        }

        var result = L.GetGlobalInteger("foo");
        Assert.Equal(10, result);
        Assert.Equal(0, L.GetTop());
    }
    
    [Fact]
    public void TestCompile2()
    {
        var L = new LuaState();
        var r1 = L.L_DoString("foo = 0");
        Assert.Equal(ThreadStatus.LUA_OK, r1);
        Assert.Equal(0, L.GetTop());
    
        var r2 = L.L_LoadString("foo = foo + 1");
        Assert.Equal(ThreadStatus.LUA_OK, r2);
        Assert.Equal(1, L.GetTop());
        Assert.Equal(LuaType.LUA_TFUNCTION, L.Type(-1));

        var fun = L.L_CheckLuaFunction(-1);
        L.Pop(1);

        for (var i = 0; i < 10; i++)
        {
            L.PushLuaFunction(fun);
            L.Call(0, 0);
        }

        var result = L.GetGlobalInteger("foo");
        Assert.Equal(10, result);
        Assert.Equal(0, L.GetTop());
    }
}