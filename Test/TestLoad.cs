using CSLua;
using CSLua.Extensions;
using CSLua.Parse;
using Xunit.Sdk;

namespace Test;

public sealed class TestLoad
{
    [Fact]
    public void Test1()
    {
        var L = Lua.New();
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
        var L = Lua.New();
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
        var L = Lua.New();
        var proto = Parser.Read("foo = 1").Proto;
        var res = L.DoProto(proto, "Test");
        Assert.Equal(ThreadStatus.LUA_OK, res);
        
        L.GetGlobal("foo");
        var ret = L.ToInteger(-1);
        Assert.Equal(1, ret);
    }

    private volatile bool _bilDone;
    
    [Fact]
    public void BugInfiniteLoop()
    {
        _bilDone = false;
        var L = Lua.New();
        L.OpenLibs();
        
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(500);
            Assert.True(_bilDone);
        });

        L.Eval("assert(not load(function () return true end))");
        _bilDone = true;
    }
}