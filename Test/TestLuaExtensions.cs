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
}