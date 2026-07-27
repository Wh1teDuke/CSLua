using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestBit
{
    [Fact]
    public void TestAnd1()
    {
        var L = Lua.New();
        L.OpenLibs();
        L.Eval("assert(bit32.band(-1) == 0xffffffff, bit32.band(-1))");
    }
}