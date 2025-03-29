using CSLua;

namespace Test;

public sealed class TestCall
{
    [Fact]
    public void Test1()
    {
        var L = new LuaState();
        
        Assert.Equal(0, L.GetTop());
        var res = L.L_DoString("function add(a, b) return a + b end");
        Assert.Equal(ThreadStatus.LUA_OK, res);
        Assert.Equal(0, L.GetTop());

        L.GetGlobal("add");
        L.PushInteger(1);
        L.PushInteger(2);
        Assert.Equal(3, L.GetTop());
        L.Call(2, 1);
        Assert.Equal(1, L.GetTop());
        Assert.Equal(ThreadStatus.LUA_OK, L.Status);
        var i = L.ToInteger(-1);
        L.Pop(1);
        Assert.Equal(3, i);
        Assert.Equal(0, L.GetTop());
    }
    
    [Fact]
    public void Test2()
    {
        var L = new LuaState();
        var res = L.L_DoString("function add(a, b) return a + b end");
        Assert.Equal(ThreadStatus.LUA_OK, res);

        L.GetGlobal("add");
        var r = L.L_Ref(LuaDef.LUA_REGISTRYINDEX);
        L.Pop(-1);
        L.RawGetI(LuaDef.LUA_REGISTRYINDEX, r);
        L.PushInteger(1);
        L.PushInteger(2);
        L.Call(2, 1);
        Assert.Equal(ThreadStatus.LUA_OK, L.Status);
        var i = L.ToInteger(-1);
        L.Pop(1);
        L.L_Unref(LuaDef.LUA_REGISTRYINDEX, r);
        Assert.Equal(3, i);
    }

    [Fact]
    public void TestCSClosure1()
    {
        var L = new LuaState();
        L.PushCSharpFunction(TestFunction);
        Assert.Equal(1, L.GetTop());
        L.SetGlobal("inc");
        Assert.Equal(0, L.GetTop());

        var res = L.L_DoString("return inc(1)");
        Assert.Equal(ThreadStatus.LUA_OK, res);
        Assert.Equal(LuaType.LUA_TNUMBER, L.Type(-1));

        var r = L.ToInteger(-1);
        Assert.Equal(2, r);
        return;

        int TestFunction(ILuaState L)
        {
            Assert.Equal(1, L.GetTop());
            Assert.Equal(LuaType.LUA_TNUMBER, L.Type(1));
            Assert.True(L.TestStack([LuaType.LUA_TNUMBER]));
            var i = L.ToInteger(1);
            L.PushInteger(i + 1);
            return 1;
        }
    }
    
    [Fact]
    public void TestCSClosure2()
    {
        var L = new LuaState();
        L.PushCSharpFunction(TestFunction);
        Assert.Equal(1, L.GetTop());
        L.SetGlobal("inc");
        Assert.Equal(0, L.GetTop());

        var res = L.L_DoString("return inc(1, true)");
        Assert.Equal(ThreadStatus.LUA_OK, res);
        Assert.Equal(LuaType.LUA_TNUMBER, L.Type(-1));

        var r = L.ToInteger(-1);
        Assert.Equal(2, r);
        return;

        int TestFunction(ILuaState L)
        {
            Assert.Equal(2, L.GetTop());
            Assert.Equal(LuaType.LUA_TNUMBER, L.Type(1));
            Assert.Equal(LuaType.LUA_TBOOLEAN, L.Type(2));
            Assert.True(L.TestStack(
                [LuaType.LUA_TNUMBER, LuaType.LUA_TBOOLEAN]));
            var i = L.ToInteger(1);
            L.PushInteger(i + 1);
            return 1;
        }
    }

    [Fact]
    public void TestFuncData1()
    {
        const int id = 123;
        var L = new LuaState();
        L.NewTable();
        Assert.Equal(1, L.GetTop());
        L.PushCSharpFunction(GetID);
        Assert.Equal(2, L.GetTop());
        L.SetField(-2, "GetID");
        Assert.Equal(1, L.GetTop());
        L.SetGlobal("MyObj");
        Assert.Equal(0, L.GetTop());

        var res = L.L_DoString("return MyObj.GetID()");
        Assert.Equal(ThreadStatus.LUA_OK, res);
        
        var id2 = L.ToInteger(-1);
        Assert.Equal(id, id2);
        return;
        
        int GetID(ILuaState L)
        {
            Assert.Equal(0, L.GetTop());
            L.PushInteger(id);
            return 1;
        }
    }
    
    [Fact]
    public void TestMethodData2()
    {
        const int id = 123;
        var L = new LuaState();
        L.NewTable();
        Assert.Equal(1, L.GetTop());
        L.PushCSharpFunction(GetID);
        Assert.Equal(2, L.GetTop());
        L.SetField(-2, "GetID");
        Assert.Equal(1, L.GetTop());
        L.SetGlobal("MyObj");
        Assert.Equal(0, L.GetTop());

        var res = L.L_DoString("return MyObj:GetID()");
        Assert.Equal(ThreadStatus.LUA_OK, res);
        
        var id2 = L.ToInteger(-1);
        Assert.Equal(id, id2);
        return;
        
        int GetID(ILuaState L)
        {
            Assert.Equal(1, L.GetTop());
            Assert.Equal(LuaType.LUA_TTABLE, L.Type(1));
            L.PushInteger(id);
            return 1;
        }
    }

    [Fact]
    public void TestMethodData3()
    {
        new LuaState().Eval(
        """
        function foo() return 1 end
        foo()
        """);
    }
}