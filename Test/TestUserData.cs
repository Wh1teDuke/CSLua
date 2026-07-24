using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestUserData
{
    private sealed class TUserData : IUSerData
    {
        public LuaTable? MetaTable { get; set; }
        public int Length => 777;

        public int Val;

        public TUserData(LuaState L, int val)
        {
            MetaTable = new LuaTable(L);
            MetaTable.Set("__add", L =>
            {
                L.CheckAny(2);

                var a1 = L.CheckUserData(1) as TUserData;
                var a2 = L.CheckInteger(2);

                if (a1 == null)
                {
                    L.PushNil();
                    return 1;
                }
                
                L.PushNumber(a1.Val + a2);
                return 1;
            });
            
            MetaTable.Set("__index", L =>
            {
                L.CheckAny(2);

                var a1 = L.CheckUserData(1) as TUserData;
                var key = L.CheckString(2);
                
                if (a1 == null)
                {
                    L.PushNil();
                    return 1;
                }

                if (key == "val")
                {
                    L.PushInteger(a1.Val);
                    return 1;
                }

                return 0;
            });
            
            MetaTable.Set("__newindex", L =>
            {
                L.CheckAny(2);

                var a1 = L.CheckUserData(1) as TUserData;
                var key = L.CheckString(2);
                
                if (a1 == null)
                    return 0;

                if (key == "val")
                {
                    var val = L.CheckInteger(3);
                    a1.Val = val;
                }

                return 0;
            });
            
            Val = val;
        }
    }
    
    [Fact]
    public void Test1()
    {
        var L = Lua.New();
        var udata = new TUserData(L, 9);
        L.SetGlobal("udata", udata);
        
        L.Eval("return #udata");
        var udataLength = L.PopInteger();
        Assert.Equal(udata.Length, udataLength);
        
        L.Eval("return udata == udata");
        var udataEq = L.PopBool();
        Assert.Equal(true, udataEq);
        
        L.Eval("return udata + 1");
        var udataAdd = L.PopInteger();
        Assert.Equal(udata.Val + 1, udataAdd);
        
        L.Eval("return udata.val");
        var udataVal = L.PopInteger();
        Assert.Equal(udata.Val, udataVal);
        
        L.Eval("return udata.val2");
        var udataNil = L.IsNil(-1);
        Assert.True(udataNil);
        
        L.Eval("udata.val = 123");
        Assert.Equal(123, udata.Val);
        
        L.Eval(
            """
            udata.val2 = 123
            return udata.val2
            """
            );
        udataNil = L.IsNil(-1);
        Assert.True(udataNil);
    }
}