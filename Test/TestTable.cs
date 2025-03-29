using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestTable
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
        L.Eval("""
               local t = {foo = "foo", bar = "bar"}
               return t;
               """);

        var t = L.PopTable();
        var optTVal1 = t.TryGet("foo");
        var optTVal2 = t.TryGet("bar");

        var foo = Assert.NotNull(optTVal1);
        var bar = Assert.NotNull(optTVal2);

        Assert.Equal("foo", foo.SValue());
        Assert.Equal("bar", bar.SValue());
    }

    [Fact]
    public void Test2()
    {
        var L = new LuaState();
        L.Eval("""
                local t = {};
                t.foo = "bar";
                return t.foo;
               """);
        var bar = L.PopString();
        Assert.Equal("bar", bar);
    }
    
    [Fact]
    public void Test3()
    {
        var L = new LuaState();
        L.Eval("""
                local t = {};
                t.foo = "bar";
                return t["foo"];
               """);
        var bar = L.PopString();
        Assert.Equal("bar", bar);
    }
    
    [Fact]
    public void Test4()
    {
        var L = new LuaState();
        L.Eval("""
                local t = {};
                t.foo = {["bar"]="bar"};
                function getFoo(t)
                  return t.foo;
                end
                return getFoo(t);
               """);
        var t = L.PopTable();
        var optTVal1 = t.TryGet("bar");
        var bar = Assert.NotNull(optTVal1);
        Assert.Equal("bar", bar.SValue());
    }

    [Fact]
    public void TestBigTable1()
    {
        var L = new LuaState();
        L.Eval("""
                local t = {};
                for i = 1, 10_000 do
                  t["key" .. i] = i
                end
                return t;
               """);

        var t = L.PopTable();
        for (var i = 1; i <= 10_000; i++)
        {
            var optTVal1 = t.TryGet("key" + i);
            var val = Assert.NotNull(optTVal1);
            Assert.Equal(i, (int)val.NValue);
        }
    }
}