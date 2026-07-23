using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestArith
{
    [Fact]
    public void Test1()
    {
        var L = Lua.New();
        L.Eval("""
               r1 = 2 - 1
               r2 = 2 + 1
               r3 = 2 * 2
               r4 = 2 / 2
               """);
        
        var r1 = L.GetGlobalInteger("r1")!;
        var r2 = L.GetGlobalInteger("r2")!;
        var r3 = L.GetGlobalInteger("r3")!;
        var r4 = L.GetGlobalInteger("r4")!;
        
        Assert.Equal(1, r1);
        Assert.Equal(3, r2);
        Assert.Equal(4, r3);
        Assert.Equal(1, r4);
    }

    [Fact]
    public void Test2()
    {
        var L = Lua.New();
        L.Eval("""
                r1 = 1 < 2
                r2 = 1 <= 2
                r3 = 1 > 2
                r4 = 1 >= 2
                """);

        var r1 = L.GetGlobalBool("r1")!;
        var r2 = L.GetGlobalBool("r2")!;
        var r3 = L.GetGlobalBool("r3")!;
        var r4 = L.GetGlobalBool("r4")!;
        
        Assert.True(r1);
        Assert.True(r2);
        Assert.False(r3);
        Assert.False(r4);
    }

    [Fact]
    public void Test3()
    {
        const int LoopCount = 50_000;
        var sum1 = 0;
        for (var i = 1; i <= LoopCount; i++) sum1 += i;
        
        var L = Lua.New();
        L.DoString($"""
                   local count = {LoopCount}
                   local sum = 0
                   for i = 1, count do
                       sum = sum + i
                   end
                   return sum
                   """);
        var sum2 = L.PopNumber();
        Assert.Equal(sum1, sum2);
    }
    
    [Fact]
    public void Test4()
    {
        const int LoopCount = 50_000;
        var sum1 = 0;
        for (var i = 1; i <= LoopCount; i++) sum1 += i;
        
        var L = Lua.New();
        L.DoString("""
                   function AddLoop(count)
                       local sum = 0
                       for i = 1, count do
                           sum = sum + i
                       end
                       return sum
                   end
                   """);
        
        L.GetGlobal("AddLoop");
        L.PushNumber(LoopCount);
        L.Call(1, 1);
        
        var sum2 = L.Status != ThreadStatus.LUA_OK 
            ? throw new Exception(L.PopErrorMsg()) 
            : L.PopNumber();

        Assert.Equal(sum1, sum2);
    }
}