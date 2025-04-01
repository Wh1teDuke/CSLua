using CSLua;

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

    private static void Run(string file)
    {
        var L = new LuaState();
        L.L_OpenLibs();
        
        if (file == "strings.lua")
            L.Eval("_no32 = true");
        else if (file == "nextvar.lua")
            L.Eval("_port = true");
        
        var r = L.L_DoFile(Path.Join("suite", file));
        if (ThreadStatus.LUA_OK != r) Assert.Fail(L.ToString(1));
    }
}