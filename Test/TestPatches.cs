using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestPatches
{
    [Fact]
    public void TestNE()
    {
        var L = Lua.New();
        L.Eval("return false ~= true");
        Assert.Equal(Lua.Type.LUA_TBOOLEAN, L.Type(-1));
        Assert.True(L.PopBool());
        L.Eval("return false != true");
        Assert.Equal(Lua.Type.LUA_TBOOLEAN, L.Type(-1));
        Assert.True(L.PopBool());
    }

    [Fact]
    public void TestCompoundAssignment()
    {
        var L = Lua.New();
        L.Eval("local foo = 1; foo += 1; return foo;");
        Assert.Equal(2, L.PopInteger());
        
        L.Eval("local foo = {bar=1}; foo.bar += 1; return foo.bar;");
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
        var L = Lua.New();
        L.Eval("return 1_000;");
        Assert.Equal(1000, L.PopInteger());
    }

    [Fact]
    public void TestContinue1()
    {
        var L = Lua.New();
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
        var L = Lua.New();
        L.OpenLibs();

        var r1 = L.DoFile(Path.Join("lua", "continue_valid.lua"));
        Assert.Equal(ThreadStatus.LUA_OK, r1);
    }

    [Fact]
    public void TestImplicitIter()
    {
        // Self-iterating Objects
        // http://lua-users.org/files/wiki_insecure/power_patches/5.2/jh-lua-iter-5.2.patch
        var L = Lua.New();
        L.OpenLibs();

        L.Eval("""
                local res = 0
                for i, v in {1, 2, 3, 4} do
                  res += v
                end
                return res
                """);
        var i = L.PopInteger()!;
        Assert.Equal(10, i);
    }

    [Fact]
    public void TestStringConcatPlus1()
    {
        var L = Lua.New();
        L.OpenLibs();
        L.Eval("return 'foo' + 'bar'");
        var foobar = L.PopString();
        Assert.Equal("foobar", foobar);
    }
    
    [Fact]
    public void TestStringConcatPlus2()
    {
        var L = Lua.New();
        L.OpenLibs();
        L.Eval("""
               local res = 'foo'
               res += 'bar'
               return res
               """);
        var foobar = L.PopString();
        Assert.Equal("foobar", foobar);
    }
    
    [Fact]
    public void TestStringConcatPlus3()
    {
        var L = Lua.New();
        L.OpenLibs();
        L.Eval("return 'foo' + true");
        var foobar = L.PopString();
        Assert.Equal("footrue", foobar);
    }
    
    [Fact]
    public void TestStringConcatPlus4()
    {
        var L = Lua.New();
        L.OpenLibs();
        L.Eval("return 'foo' + 1 + true + false + 'bar'");
        var foobar = L.PopString();
        Assert.Equal("foo1truefalsebar", foobar);
    }

    [Fact]
    public void TestConcatBool1()
    {
        var L = Lua.New();
        L.Eval("return 'foo' .. true");
        var foobar = L.PopString();
        Assert.Equal("footrue", foobar);
    }
    
    [Fact]
    public void TestConcatBool2()
    {
        var L = Lua.New();
        L.Eval("return true .. false");
        var foobar = L.PopString();
        Assert.Equal("truefalse", foobar);
    }

    [Fact]
    public void TestComment()
    {
        var L = Lua.New();
        L.Eval("""
               // Foo
               local a = 1 // Foo
               // Bar
               local b = 2 // Bar
               // Baz
               return a + b
               """);
        var res = L.PopInteger();
        Assert.Equal(3, res);
    }
    [Fact]
    public void TestMulti1()
    {
        var L = Lua.New();
        L.Eval("""
               /* Foo
               */
               local a = 1 /* Foo
               -- Bar*/
               local b = 2 /* Bar
               -- Baz*/
               return a + b
               """);
        var res = L.PopInteger();
        Assert.Equal(3, res);
    }
}