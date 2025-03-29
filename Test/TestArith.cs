using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestArith
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
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
        var L = new LuaState();
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
}