using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestString
{
    [Fact]
    public void TestConcat1()
    {
        var L = Lua.New();
        L.Eval("return 'foo' .. 'bar'");
        var foobar = L.PopString();
        Assert.Equal("foobar", foobar);
    }
    
    [Fact]
    public void TestConcat2()
    {
        // TODO: overload '+' for string concatenation
        const int LoopCount = 5_000;
        
        var str1 = "";
        for (var i = 0; i < LoopCount; i++) str1 += i;
        
        var L = Lua.New();
        L.DoString("""
                   function AddLoop(count)
                       local sum = ""
                       for i = 0, count - 1 do
                           sum = sum .. i
                       end
                       return sum
                   end
                   """);
        
        L.GetGlobal("AddLoop");
        L.PushNumber(LoopCount);
        L.Call(1, 1);
        
        var str2 = L.Status != ThreadStatus.LUA_OK 
            ? throw new Exception(L.PopErrorMsg()) 
            : L.PopString()!;

        Assert.Equal(str1, str2);
    }
}