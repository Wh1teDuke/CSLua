using CSLua;
using CSLua.Extensions;
using CSLua.Parse;
using CSLua.Util;

namespace Test;

public static class TestError
{
    [Fact]
    public static void Test1()
    {
        var msg = Assert
            .ThrowsAny<LuaException>(() => Parse("foobar"));
        Assert.Equal(
            "Test:1: Syntax error: Expected VCALL, got VINDEXED", msg.Message);
    }
    
    [Fact]
    public static void Test2()
    {
        var msg = Assert
            .ThrowsAny<LuaException>(() => Parse("[[foobar"));
        Assert.Equal(
            "Test:1: Unfinished long string/comment:([[foobar, EOS)", msg.Message);
    }
    
    [Fact]
    public static void Test3()
    {
        var msg = Assert
            .ThrowsAny<LuaException>(() => Parse("function 1Invalid1() end"));
        Assert.Equal(
            "Test:1: Syntax error: Expected function name, got TokenNumber: 1", msg.Message);
    }

    [Fact]
    public static void Test4()
    {
        var L = new LuaState();
        L.PushCSharpFunction(DoTraceBack);
        L.SetGlobal("doTraceBack");
        L.Eval("""
               function foo()
                    return doTraceBack()
               end
               return foo()
               """);
        var msg = L.PopString();
        Assert.Equal(
            "stack traceback:\n\t[C#]: in function 'doTraceBack'\n\t[source \"function foo()...\"]:2: in function 'foo'\n\t(...tail calls...)",
            msg);
    }

    private static int DoTraceBack(ILuaState L)
    {
        L.PushString(LuaUtil.Traceback((LuaState)L));
        return 1;
    }
    
    [Fact]
    public static void Test5()
    {
        var e = Assert.ThrowsAny<LuaException>(
            () =>
        Eval("""
             function foo()
               function bar()
                 local v = nil
                 print(v[0])
               end
               bar()
             end
             foo()
             """));
        Assert.Equal(
           "print(v[0]):4: Attempt to index a nil value",
           e.Message);
    }

    [Fact]
    public static void Test6()
    {
        var e = Assert.ThrowsAny<LuaException>(() =>
        Eval("local bar=123;function foo() return #bar end; foo()")
        );
        Assert.Equal(
            "local bar=123;function foo() return #bar end; foo():1: Attempt to get length of upvalue 'bar' (a number value)",
            e.Message);
    }

    [Fact]
    public static void TestCallNil()
    {
        var e = Assert.ThrowsAny<LuaException>(() =>
            Eval("foobar()"));
        Assert.StartsWith("foobar():1: Attempt to call a nil value", e.Message);
    }
    
    [Fact]
    public static void TestParserCompoundAssBug1()
    {
        // Comp ass bug
        var L = new LuaState();
        var e = Assert.ThrowsAny<LuaException>(() =>
            L.Eval("for i = 1, 10 do res += i end"));
        Assert.StartsWith("for i = 1, 10 do res += i end:1: Attempt to Perform arithmetic on a nil value", e.Message);
    }

    [Fact]
    public static void TestEvalWithTraceback()
    {
        var L = new LuaState();
        var e = Assert.ThrowsAny<LuaException>(() =>
            L.Eval("""
                   function foo()
                     bar()
                   end
                   foo();
                   """, LuaUtil.TracebackErrHandler));
        Assert.Equal(
            "bar():2: Attempt to call a nil value\nstack traceback:\n\t[source \"function foo()...\"]:2: in function 'foo'\n\t[source \"function foo()...\"]:4: in main chunk",
            e.Message);
    }

    private static void Parse(string src) => Parser.Read(src, "Test");
    private static void Eval(string src) => new LuaState().Eval(src);
}