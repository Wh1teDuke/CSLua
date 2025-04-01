using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestUpvalue
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
        L.DoString("""
                     local foo = 123
                     function bar() foo = 456 end
                     bar()
                     return foo
                     """);
        var r = L.PopInteger();
        Assert.Equal(456, r);
    }
    
    [Fact]
    public void Test2()
    {
        var L = new LuaState();
        L.DoString("""
                     local foo = 123
                     function bar()
                        function baz() foo = 456 end 
                        baz()
                     end
                     bar()
                     return foo
                     """);
        var r = L.PopInteger();
        Assert.Equal(456, r);
    }

    [Fact]
    public void Test3()
    {
        var L = new LuaState();
        L.DoString("""
                     local foo = 0
                     function bar()
                        function baz() foo = foo + 1 end 
                        baz()
                     end
                     for i = 1, 10 do
                        bar()
                     end
                     return foo
                     """);
        var r = L.PopInteger();
        Assert.Equal(10, r);
    }
    
    [Fact]
    public void Test4()
    {
        var L = new LuaState();
        L.Eval(
            """
            local i = 0
            function a()
                i += 1;
                function b()
                    i += 1
                    function c() return i + 1 end
                    return c() + 1
                end
                return b() + 1
            end
            return a() + 1
            """);
        var i = L.PopInteger();
        Assert.Equal(6, i);
    }

    [Fact]
    public void Test5()
    {
        var L = new LuaState();
        L.Eval(
            """
            function a()
                local i = 1
                function b()
                    i += 1
                    return i
                end
                return b
            end
            local b1 = a()
            local b2 = a()
            return b1(), b2()
            """);

        var b1 = L.PopInteger();
        var b2 = L.PopInteger();

        Assert.Equal(2, b1);
        Assert.Equal(2, b2);
    }
}