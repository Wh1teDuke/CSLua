using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestFunction
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
        L.Eval(
            """
            local bar = 0
            for i = 1, 10 do
              function foo() bar = bar + 1 end
              foo()
            end
            return bar
            """);
        Assert.Equal(10, L.PopInteger());
    }
    
    [Fact]
    public void Test2()
    {
        var L = new LuaState();
        L.Eval(
            """
            bar = 10
            function foo() bar = bar + 1 end
            foo()
            return bar
            """);
        Assert.Equal(11, L.PopInteger());
    }

    [Fact]
    public void Test3()
    {
        var L = new LuaState();
        L.Eval(
            """
            function a(i)
                function b(i)
                    function c(i) return i + 1 end
                    return c(i) + 1
                end
                return b(i) + 1
            end
            return a(0) + 1
            """);
        var i = L.PopInteger();
        Assert.Equal(4, i);
    }

    [Fact]
    public void TestErrorHandler1()
    {
        string? error = null;
        var L = new LuaState();
        L.L_OpenLibs();
        L.PushCSharpFunction(OnError);
        var eIdx = L.GetTop();

        Assert.Equal(ThreadStatus.LUA_OK, L.L_LoadString("error('FooBar')"));
        var err = L.PCallK(0, 0, eIdx, -1, null);
        Assert.Equal(ThreadStatus.LUA_ERRRUN, err);
        Assert.StartsWith("[source \"error('FooBar')\"]:1: FooBar", error);

        return;
        int OnError(ILuaState lua)
        {
            error = lua.ToString(1);
            return 0;
        }
    }
}