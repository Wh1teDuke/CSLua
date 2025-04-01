using CSLua;

namespace Test;

public sealed class TestInt64
{
    [Fact]
    public void TestInt1()
    {
        var L = new LuaState();
        L.L_OpenLibs();
        
        L.Eval("""
               local i1 = math.tointeger(5)
               local i2 = math.tointeger(6)
               return i1 + i2, i1 - i2, i1 * i2, i1 / i2, -i1
               """);
        var iAdd = L.L_CheckInt64(-5);
        var iSub = L.L_CheckInt64(-4);
        var iMul = L.L_CheckInt64(-3);
        var iDiv = L.L_CheckInt64(-2);
        var iNeg = L.L_CheckInt64(-1);
        
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
        L.L_OpenLibs();
        
        L.Eval("""
               local i1 = math.tointeger(5)
               local i2 = 6
               return i1 + i2, i1 - i2, i1 * i2, i1 / i2
               """);
        var iAdd = L.L_CheckNumber(-4);
        var iSub = L.L_CheckNumber(-3);
        var iMul = L.L_CheckNumber(-2);
        var iDiv = L.L_CheckNumber(-1);
        
        Assert.Equal(11, iAdd);
        Assert.Equal(-1, iSub);
        Assert.Equal(30, iMul);
        Assert.Equal((5 / 6.0), iDiv);
    }
}