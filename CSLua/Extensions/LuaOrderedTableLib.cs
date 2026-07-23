namespace CSLua.Extensions;

public static class LuaOrderedTableLib
{
    public const string LIB_NAME = "otable";
    
    public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);
    
    public static int OpenLib(LuaState lua)
    {
        ReadOnlySpan<NameFuncPair> define =
        [
            new("new",          OTableNew),
            new("remove",       OTableRemove),
            new("removeat",     OTableRemoveAt),
            new("haskey",       OTableHasKey),
            new("hasval",       OTableHasVal),
            new("addall",       OTableAddAll),
            new("indexof",      OTableIndexOf),
            new("get",          OTableGet),
            new("getat",        OTableGetAt),
            new("set",          OTableSet),
            new("length",       OTableLength),
            new("clear",        OTableClear),
            new("isempty",      OTableIsEmpty),
            new("next",         OTableNext),
            new("pairs", 	    OTablePairs),
            new("ipairs", 	    OTableIPairs),
        ];

        lua.NewLib(define);
        return 1;
    }
    
    private static int OTableNew(LuaState lua)
    {
        var otable = new OrderedDictionary<TValue, TValue>();
        lua.PushLightUserData(otable);
        return 1;
    }
    
    private static int OTableRemove(LuaState lua)
    {
        var otable = GetTable(lua, 1);
        GetValue(lua, 2, out var key);

        var res = otable.Remove(key.V);
        lua.PushBoolean(res);
        return 1;
    }
    
    private static int OTableRemoveAt(LuaState lua)
    {
        var otable = GetTable(lua, 1);
        var index = lua.CheckInteger(2);

        otable.RemoveAt(index);
        return 0;
    }
    
    private static int OTableHasKey(LuaState lua)
    {
        var otable = GetTable(lua, 1);
        GetValue(lua, 2, out var key);

        var res = otable.ContainsKey(key.V);
        lua.PushBoolean(res);
        return 1;
    }
    
    private static int OTableHasVal(LuaState lua)
    {
        var otable = GetTable(lua, 1);
        GetValue(lua, 2, out var val);

        var res = otable.ContainsValue(val.V);
        lua.PushBoolean(res);
        return 1;
    }
    
    private static int OTableAddAll(LuaState lua)
    {
        var otable1 = GetTable(lua, 1);
        var top = lua.GetTop();

        for (var i = 2; i <= top; i++)
        {
            var otable2 = GetTable(lua, i);
            foreach (var entry in otable2)
                otable1.Add(entry.Key, entry.Value);
        }

        return 0;
    }
    
    private static int OTableIndexOf(LuaState lua)
    {
        var otable1 = GetTable(lua, 1);
        GetValue(lua, 2, out var key);

        var idx = otable1.IndexOf(key.V);
        lua.PushInteger(idx);

        return 1;
    }
    
    private static int OTableGet(LuaState lua)
    {
        var otable = GetTable(lua, 1);
        GetValue(lua, 2, out var key);
        var val = otable[key.V];
        lua.PushTValue(val);
        return 1;
    }
    
    private static int OTableGetAt(LuaState lua)
    {
        var otable = GetTable(lua, 1);
        var index = lua.CheckInteger(2);
        var val = otable.GetAt(index).Value;
        lua.PushTValue(val);
        return 1;
    }

    private static int OTableSet(LuaState lua)
    {
        var otable = GetTable(lua, 1);
        GetValue(lua, 2, out var key);
        GetValue(lua, 3, out var val);

        var res = otable.TryAdd(key.V, val.V);
        lua.PushBoolean(res);

        return 1;
    }
    
    private static int OTableClear(LuaState lua)
    {
        var otable = GetTable(lua, 1);
        otable.Clear();
        return 0;
    }
    
    private static int OTableIsEmpty(LuaState lua)
    {
        var otable = GetTable(lua, 1);
        lua.PushBoolean(otable.Count == 0);
        return 1;
    }

    private static int OTableLength(LuaState lua)
    {
        var otable = GetTable(lua, 1);
        lua.PushInteger(otable.Count);
        return 1;
    }

    private static int OTableINext(LuaState lua)
    {
        var L = lua;
        lua.SetTop(2);

        var otable = GetTable(lua, 1);
        var key = L.Ref((L.TopIndex - 1));
        var index = 0;

        if (key.V.IsNil())
            index = -1;
        else
            index = lua.ToInteger(-1);

        if (index < otable.Count - 1)
        {
            index++;
            key.SetDouble(index);
            var val = otable.GetAt(index).Value;
            L.Top.Set(val);
            L.ApiIncrTop();
            return 2;
        }

        lua.Pop(1);
        lua.PushNil();
        return 1;
    }
    
    private static int OTableNext(LuaState lua)
    {
        var L = lua;
        lua.SetTop(2);

        var otable = GetTable(lua, 1);
        var key = L.Ref((L.TopIndex - 1));
        var index = 0;

        if (!key.V.IsNil())
        {
            var currentIndex = otable.IndexOf(key.V);
            if (currentIndex == -1) return 0;
            index = currentIndex + 1;
        }

        if (index < otable.Count)
        {
            var kvp = otable.GetAt(index);
            L.PushTValue(kvp.Key);
            L.PushTValue(kvp.Value);
            return 2;
        }

        return 0;
    }

    private static readonly CsClosure NextClosure = new (OTableNext);
    private static readonly CsClosure INextClosure = new (OTableINext);

    private static int OTablePairs(LuaState lua)
    {
        GetTable(lua, 1);
        lua.PushCsClosure(NextClosure);
        lua.PushValue(1);
        lua.PushNil();

        return 3;
    }
    
    private static int OTableIPairs(LuaState lua)
    {
        GetTable(lua, 1);
        lua.PushCsClosure(INextClosure);
        lua.PushValue(1);
        lua.PushNil();

        return 3;
    }

    private static OrderedDictionary<TValue, TValue> GetTable(LuaState lua, int index)
    {
        if (lua.CheckUserData(index) is OrderedDictionary<TValue, TValue> table)
            return table;

        lua.ArgError(index, "expected an ordered table");
        return null!;
    }

    private static void GetValue(LuaState lua, int index, out StkId id)
    {
        if (!lua.Index2Addr(index, out id))
            lua.Error("Can't access variable");
    }
}