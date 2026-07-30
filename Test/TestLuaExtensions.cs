using CSLua;
using CSLua.Extensions;
using CSLua.Util;

namespace Test;

public sealed class TestLuaExtensions
{
    [Fact]
    public void TestEvalPopOnError1()
    {
        var L = Lua.New();
        var top1 = L.GetTop();

        try
        {
            L.Eval("error()", _ => 0);
            Assert.Fail("LuaRuntimeException expected");
        }
        catch (LuaRuntimeException)
        {
            var top2 = L.GetTop();
            Assert.Equal(top1, top2);
        }
    }
    
    [Fact]
    public void TestEvalPopOnError2()
    {
        var L = Lua.New();
        var top1 = L.GetTop();

        try
        {
            L.Eval("error()");
            Assert.Fail("LuaRuntimeException expected");
        }
        catch (LuaRuntimeException)
        {
            var top2 = L.GetTop();
            Assert.Equal(top1, top2);
        }
    }

    [Fact]
    public void TestCallLuaClosure1()
    {
        var L = Lua.New();
        var closure = L.Compile("return 1 + 2");
        
        L.Call(closure, 0, 1);
        var res = L.PopInteger();
        Assert.Equal(3, res);
    }
    
    [Fact]
    public void TestCallLuaClosure2()
    {
        var L = Lua.New();
        L.OpenLibs();
        L.Eval( """
                    return function(a, b)
                        assert(type(a) == "number", type(a))
                        assert(type(b) == "string", type(b)) 
                        return 1, 2, 3
                    end
                   """);
        var closure = L.PopLuaClosure()!;
        
        Span<TValue> results = [0, 0, 0];
        L.Call(closure, [1, "foobar"], results);
        Assert.Equal([1, 2, 3], results);
    }

    [Fact]
    public void TestPushAction()
    {
        var L = Lua.New();
        var test1 = false; 
        var test2 = false; 

        L.OpenLibs();
        L.SetGlobal("test1", () => test1 = true);
        L.SetGlobal("test2", _ => test2 = true);
        L.Eval("test1();test2();");
        Assert.True(test1);
        Assert.True(test2);
    }
}