using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestCompile
{
    [Fact]
    public void TestCompile1()
    {
        var L = Lua.New();
        var r1 = L.DoString("foo = 0");
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
        var L = Lua.New();
        var r1 = L.DoString("foo = 0");
        Assert.Equal(ThreadStatus.LUA_OK, r1);
        Assert.Equal(0, L.GetTop());
    
        var r2 = L.LoadString("foo = foo + 1");
        Assert.Equal(ThreadStatus.LUA_OK, r2);
        Assert.Equal(1, L.GetTop());
        Assert.Equal(Lua.Type.LUA_TFUNCTION, L.Type(-1));

        var fun = L.CheckLuaFunction(-1);
        L.Pop(1);

        for (var i = 0; i < 10; i++)
        {
            L.PushLuaClosure(fun);
            L.Call(0, 0);
        }

        var result = L.GetGlobalInteger("foo");
        Assert.Equal(10, result);
        Assert.Equal(0, L.GetTop());
    }

    [Fact]
    public void TestCompile3()
    {
        var L = Lua.New();
        L.DoString("dict = {Foo='Bar',Foo1='Bar1',Foo2='Bar2',Foo3='Bar3'}");
        L.Compile("_dict", "return dict.Foo");
        L.GetGlobal("_dict");
        L.Call(0, 1);
        var res = L.PopString();
        Assert.Equal("Bar", res);
    }
}