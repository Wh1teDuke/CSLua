namespace CSLua.Extensions;

public static class LuaOrderedTableLib
{
    public const string LIB_NAME = "otable";
    
    public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);
    
    public static int OpenLib(ILuaState lua)
    {
        Span<NameFuncPair> define =
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
        ];

        lua.NewLib(define);
        return 1;
    }
    
    private static int OTableNew(ILuaState lua)
    {
        var otable = new OrderedDictionary<TValue, TValue>();
        lua.PushLightUserData(otable);
        return 1;
    }
    
    private static int OTableRemove(ILuaState lua)
    {
        var otable = GetTable(lua, 1);
        GetValue(lua, 2, out var key);

        var res = otable.Remove(key.V);
        lua.PushBoolean(res);
        return 1;
    }
    
    private static int OTableRemoveAt(ILuaState lua)
    {
        var otable = GetTable(lua, 1);
        var index = lua.CheckInteger(2);

        otable.RemoveAt(index);
        return 0;
    }
    
    private static int OTableHasKey(ILuaState lua)
    {
        var otable = GetTable(lua, 1);
        GetValue(lua, 2, out var key);

        var res = otable.ContainsKey(key.V);
        lua.PushBoolean(res);
        return 1;
    }
    
    private static int OTableHasVal(ILuaState lua)
    {
        var otable = GetTable(lua, 1);
        GetValue(lua, 2, out var val);

        var res = otable.ContainsValue(val.V);
        lua.PushBoolean(res);
        return 1;
    }
    
    private static int OTableAddAll(ILuaState lua)
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
    
    private static int OTableIndexOf(ILuaState lua)
    {
        var otable1 = GetTable(lua, 1);
        GetValue(lua, 2, out var key);

        var idx = otable1.IndexOf(key.V);
        lua.PushInteger(idx);

        return 1;
    }
    
    private static int OTableGet(ILuaState lua)
    {
        var otable = GetTable(lua, 1);
        GetValue(lua, 2, out var key);
        var val = otable[key.V];
        ((LuaState)lua).Push(val);
        return 1;
    }
    
    private static int OTableGetAt(ILuaState lua)
    {
        var otable = GetTable(lua, 1);
        var index = lua.CheckInteger(2);
        var val = otable.GetAt(index).Value;
        ((LuaState)lua).Push(val);
        return 1;
    }

    private static int OTableSet(ILuaState lua)
    {
        var otable = GetTable(lua, 1);
        GetValue(lua, 2, out var key);
        GetValue(lua, 3, out var val);

        var res = otable.TryAdd(key.V, val.V);
        lua.PushBoolean(res);

        return 1;
    }
    
    private static int OTableClear(ILuaState lua)
    {
        var otable = GetTable(lua, 1);
        otable.Clear();
        return 0;
    }
    
    private static int OTableIsEmpty(ILuaState lua)
    {
        var otable = GetTable(lua, 1);
        lua.PushBoolean(otable.Count == 0);
        return 1;
    }

    private static int OTableLength(ILuaState lua)
    {
        var otable = GetTable(lua, 1);
        lua.PushInteger(otable.Count);
        return 1;
    }

    private static int OTableNext(ILuaState lua)
    {
        var L = (LuaState)lua;
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
            key.V.SetDouble(index);
            var val = otable.GetAt(index).Value;
            L.Top.Set(new StkId(ref val));
            L.ApiIncrTop();
            return 2;
        }

        lua.Pop(1);
        lua.PushNil();
        return 1;
    }

    private static readonly CsClosure NextClosure = new (OTableNext);

    private static int OTablePairs(ILuaState lua)
    {
        GetTable(lua, 1);
        lua.PushCsClosure(NextClosure);
        lua.PushValue(1);
        lua.PushNil();

        return 3;
    }

    private static OrderedDictionary<TValue, TValue> GetTable(ILuaState lua, int index)
    {
        if (lua.CheckUserData(index) is OrderedDictionary<TValue, TValue> table)
            return table;

        lua.ArgError(index, "expected an ordered table");
        return null!;
    }

    private static void GetValue(ILuaState lua, int index, out StkId id)
    {
        if (!((LuaState)lua).Index2Addr(index, out id))
            lua.Error("Can't access variable");
    }
}