using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestInt64
{
    [Fact]
    public void TestInt1()
    {
        var L = new LuaState();
        L.OpenLibs();
        
        L.Eval("""
               local i1 = math.tointeger(5)
               local i2 = math.tointeger(6)
               return i1 + i2, i1 - i2, i1 * i2, i1 / i2, -i1
               """);
        var iAdd = L.CheckInt64(-5);
        var iSub = L.CheckInt64(-4);
        var iMul = L.CheckInt64(-3);
        var iDiv = L.CheckInt64(-2);
        var iNeg = L.CheckInt64(-1);
        
        Assert.Equal(11, iAdd);
        Assert.Equal(-1, iSub);
        Assert.Equal(30, iMul);
        Assert.Equal(0, iDiv);
        Assert.Equal(-5, iNeg);
    }
    
    [Fact]
    public void TestInt64Double()
    {
        var L = new LuaState();
        L.OpenLibs();
        
        L.Eval("""
               local i1 = math.tointeger(5)
               local i2 = 6
               return i1 + i2, i1 - i2, i1 * i2, i1 / i2
               """);
        var iAdd = L.CheckNumber(-4);
        var iSub = L.CheckNumber(-3);
        var iMul = L.CheckNumber(-2);
        var iDiv = L.CheckNumber(-1);
        
        Assert.Equal(11, iAdd);
        Assert.Equal(-1, iSub);
        Assert.Equal(30, iMul);
        Assert.Equal((5 / 6.0), iDiv);
    }
}