using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestPatches
{
    [Fact]
    public void TestNE()
    {
        var L = new LuaState();
        L.Eval("return false ~= true");
        Assert.Equal(LuaType.LUA_TBOOLEAN, L.Type(-1));
        Assert.True(L.PopBool());
        L.Eval("return false != true");
        Assert.Equal(LuaType.LUA_TBOOLEAN, L.Type(-1));
        Assert.True(L.PopBool());
    }

    [Fact]
    public void TestCompoundAssignment()
    {
        var L = new LuaState();
        L.Eval("local foo = 1; foo += 1; return foo;");
        Assert.Equal(2, L.PopInteger());
        
        L.Eval("local foo = 2; foo -= 1; return foo;");
        Assert.Equal(1, L.PopInteger());
        
        L.Eval("local foo = 1; foo *= 2; return foo;");
        Assert.Equal(2, L.PopInteger());
        
        L.Eval("local foo = 4; foo /= 2; return foo;");
        Assert.Equal(2, L.PopInteger());
        
        L.Eval("local foo = 5; foo %= 3; return foo;");
        Assert.Equal(2, L.PopInteger());
        
        L.Eval("local foo = true; foo &= false; return foo;");
        Assert.Equal(false, L.PopBool());
        
        L.Eval("local foo = false; foo |= true; return foo;");
        Assert.Equal(true, L.PopBool());
        
        L.Eval("local foobar = 'foo'; foobar ..= 'bar'; return foobar;");
        Assert.Equal("foobar", L.PopString());
    }

    [Fact]
    public void TestNumUnderscore()
    {
        var L = new LuaState();
        L.Eval("return 1_000;");
        Assert.Equal(1000, L.PopInteger());
    }

    [Fact]
    public void TestContinue1()
    {
        var L = new LuaState();
        L.Eval("""
               local foo = 0
               for i = 1, 10 do
                 if i % 2 == 0 then continue end
                 foo += 1
               end
               return foo
               """);
        var foo = L.PopInteger();
        Assert.Equal(5, foo);
    }
    
    [Fact]
    public void TestContinue2()
    {
        var L = new LuaState();
        L.L_OpenLibs();

        var r1 = L.L_DoFile(Path.Join("lua", "continue_valid.lua"));
        Assert.Equal(ThreadStatus.LUA_OK, r1);
    }
}