using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestCoroutine
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
        L.L_OpenLibs();
        L.Eval("""
               function producer()
                   for i = 1, 5 do coroutine.yield(i) end
               end
               
               local co = coroutine.create(producer)
               local t = 0
               while true do
                   local _, value = coroutine.resume(co)
                   if coroutine.status(co) == "dead" then break end
                   t += value
               end
               return t;
               """);
        var t = L.PopInteger();
        Assert.Equal(15, t);
    }
}