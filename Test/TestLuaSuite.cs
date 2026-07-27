using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestLuaSuite
{
    [Fact]
    public void TestMath() => Run("math.lua");
    [Fact]
    public void TestStrings() => Run("strings.lua");
    [Fact]
    public void TestConstructs() => Run("constructs.lua");
    [Fact]
    public void TestNextVar() => Run("nextvar.lua");
    [Fact]
    public void TestCoroutine() => Run("coroutine.lua");
    [Fact]
    public void TestGoTo() => Run("goto.lua");
    [Fact]
    public void TestVararg() => Run("vararg.lua");
    [Fact]
    public void TestCalls() => Run("calls.lua");
    [Fact]
    public void TestLocals() => Run("locals.lua");
    [Fact]
    public void TestBitwise() => Run("bitwise.lua");
    [Fact]
    public void TestApi() => Run("api.lua");
    [Fact]
    public void TestAttrib() => Run("attrib.lua");
    [Fact]
    public void TestBig() => Run("big.lua");
    [Fact]
    public void TestFiles() => Run("files.lua");
    [Fact]
    public void TestCheckTable() => Run("checktable.lua");
    /* TODO Infinite loop [Fact]
    [Fact]
    public void TestClosure() => Run("closure.lua");*/
    [Fact]
    public void TestErrors() => Run("errors.lua");
    [Fact]
    public void TestEvents() => Run("events.lua");
    [Fact]
    public void TestLiterals() => Run("literals.lua");
    [Fact]
    public void TestPm() => Run("pm.lua");
    [Fact]
    public void TestDb() => Run("db.lua");
    [Fact]
    public void TestVeryBig() => Run("verybig.lua");
    
    // https://github.com/NLua/NLua/tree/main/tests/scripts/core
    [Fact]
    public void TestBisect() => Run("bisect.lua");
    [Fact]
    public void TestFactorial() => Run("factorial.lua");
    [Fact]
    public void TestFib() => Run("fib.lua");
    [Fact]
    public void TestFibfor() => Run("fibfor.lua");
    [Fact]
    public void TestPrintf() => Run("printf.lua");
    [Fact]
    public void TestSieve() => Run("sieve.lua");
    [Fact]
    public void TestSort() => Run("sort.lua");

    private static void Run(string file)
    {
        var L = Lua.New();
        L.OpenLibs();
        
        if (file == "strings.lua")
            L.Eval("_no32 = true");
        else if (file == "nextvar.lua")
            L.Eval("_port = true");
        
        var r = L.DoFile(Path.Join("suite", file));
        if (ThreadStatus.LUA_OK != r) Assert.Fail(L.ToString(1));
    }
}