using CSLua;
using CSLua.Extensions;
using CSLua.Parse;

namespace Test;

public sealed class TestLoad
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
        L.OpenLibs();

        var r1 = L.DoFile(Path.Join("lua", "Test1.lua"));
        Assert.Equal(ThreadStatus.LUA_OK, r1);

        var r2 = L.DoString("return tFoo + tBar");
        Assert.Equal(ThreadStatus.LUA_OK, r2);

        var ret = L.ToInteger(-1);
        Assert.Equal(3, ret);
    }
    
    [Fact]
    public void Test2()
    {
        const string foo = "foo";
        var L = new LuaState();
        L.OpenLibs();
        L.PushInteger(0);
        L.SetGlobal(foo);

        L.Eval("local foo = require 'lua.Test2'");
        var foo1 = L.GetGlobalInteger(foo);
        Assert.Equal(1, foo1);

        L.Eval("local foo = require 'lua.Test2'");
        var foo2 = L.GetGlobalInteger(foo); // No change
        Assert.Equal(foo2, foo1);
    }

    [Fact]
    public void Test3()
    {
        var L = new LuaState();
        var proto = Parser.Read("foo = 1").Proto;
        var res = L.DoProto(proto, "Test");
        Assert.Equal(ThreadStatus.LUA_OK, res);
        
        L.GetGlobal("foo");
        var ret = L.ToInteger(-1);
        Assert.Equal(1, ret);
    }
}