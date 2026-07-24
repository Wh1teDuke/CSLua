using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestUserData
{
    private sealed class TUserData : IUSerData
    {
        public LuaTable? MetaTable { get; set; }
        public int Length => 777;

        public readonly int Val;

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
    }
}