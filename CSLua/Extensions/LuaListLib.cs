using CSLua.Utils;

namespace CSLua.Extensions;

public static class LuaListLib
{
    public const string LIB_NAME = "list";
	
    public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);
    
    internal static readonly LuaCsClosureValue PairsCl = new (ListPairs);

    public static int OpenLib(ILuaState lua)
    {
        Span<NameFuncPair> define =
        [
            new("new",          ListNew),
            new("remove",       ListDel),
            new("insert",       ListInsert),
            new("contains",     ListContains),
            new("indexof",      ListIndexOf),
            new("add",          ListAdd),
            new("addall",       ListAddAll),
            new("get",          ListGet),
            new("set",          ListSet),
            new("length",       ListLength),
            new("sort",         ListSort),
            new("clear",        ListClear),
            new("isempty",      ListIsEmpty),
            new("next",         ListNext),
            new("pairs", 	    ListPairs),
        ];

        lua.NewLib(define);
        return 1;
    }

    private static int ListNew(ILuaState lua)
    {
        var L = (LuaState)lua;
        var list = new List<TValue>();
        var top = lua.GetTop();

        for (var i = 1; i <= top; i++)
        {
            GetValue(L, i, out var addr);
            list.Add(addr.V);
        }
        
        lua.PushList(list);
        return 1;
    }

    private static int ListDel(ILuaState lua)
    {
        var L = (LuaState)lua;
        var list = lua.CheckList(1);
        var index = lua.ToInteger(2);
        var value = list[index];

        if (lua.Type(3) == LuaType.LUA_TBOOLEAN && lua.ToBoolean(3))
        {
            list[index] = list[^1];
            list.RemoveAt(list.Count - 1);
        }
        else
        {
            list.RemoveAt(index);            
        }

        L.Push(value);
        return 1;
    }

    private static int ListInsert(ILuaState lua)
    {
        var L = (LuaState)lua;
        var list = lua.CheckList(1);
        var index = lua.ToInteger(2);

        GetValue(L, 3, out var addr);
        list.Insert(index, addr.V);
        return 0;
    }

    private static int ListIndexOf(ILuaState lua)
    {
        var L = (LuaState)lua;
        var list = lua.CheckList(1);

        GetValue(L, 2, out var addr);
        var value1 = addr.V;

        for (var index = 0; index < list.Count; index++)
        {
            var value2 = list[index];
            if (!value1.Equals(value2)) continue;
            lua.PushInteger(index);
            return 1;
        }

        lua.PushInteger(-1);
        return 1;
    }

    private static int ListContains(ILuaState lua)
    {
        var L = (LuaState)lua;
        var list = lua.CheckList(1);

        GetValue(L, 2, out var addr);
        var value1 = addr.V;

        foreach (var value2 in list)
        {
            if (!value1.Equals(value2)) continue;
            lua.PushBoolean(true);
            return 1;
        }

        lua.PushBoolean(false);
        return 1;
    }
    
    private static int ListAdd(ILuaState lua)
    {
        var L = (LuaState)lua;
        var list = lua.CheckList(1);
        var top = lua.GetTop();

        for (var i = 2; i <= top; i++)
        {
            GetValue(L, i, out var addr);
            list.Add(addr.V);
        }

        return 0;
    }
    
    private static int ListAddAll(ILuaState lua)
    {
        var list1 = lua.CheckList(1);
        var top = lua.GetTop();

        for (var i = 2; i <= top; i++)
        {
            var list2 = lua.CheckList(i);
            foreach (var t in list2)
                list1.Add(t);
        }

        return 0;
    }

    private static int ListGet(ILuaState lua)
    {
        var L = (LuaState)lua;
        var list = lua.CheckList(1);
        var index = lua.ToInteger(2);
        var value = list[index];
        L.Push(value);
        return 1;
    }

    private static int ListSet(ILuaState lua)
    {
        var L = (LuaState)lua;
        var list = lua.CheckList(1);
        var index = lua.ToInteger(2);

        GetValue(L, 3, out var addr);
        list[index] = addr.V;

        return 1;
    }

    private static int ListLength(ILuaState lua)
    {
        var list = lua.CheckList(1);
        lua.PushInteger(list.Count);
        return 1;
    }

    private static int ListSort(ILuaState lua)
    {
        var list = lua.CheckList(1);
        list.Sort();
        return 0;
    }
    
    private static int ListClear(ILuaState lua)
    {
        var list = lua.CheckList(1);
        list.Clear();
        return 0;
    }
    
    private static int ListIsEmpty(ILuaState lua)
    {
        var list = lua.CheckList(1);
        lua.PushBoolean(list.Count == 0);
        return 1;
    }
    
    private static int ListNext(ILuaState lua)
    {
        var L = (LuaState)lua;
        lua.SetTop(2);

        var list = lua.CheckList(1);
        var key = L.Ref[L.TopIndex - 1];
        var index = 0;

        if (key.V.IsNil())
            index = -1;
        else if (key.V.IsNumber())
            index = (int)key.V.NValue;
        else
            throw new LuaException("Integer index expected, got: " + key.V);

        if (index < list.Count - 1)
        {
            index++;
            key.V.SetDouble(index);
            var val = list[index];
            L.Top.Set(new StkId(ref val));
            L.ApiIncrTop();
            return 2;
        }

        lua.Pop(1);
        lua.PushNil();
        return 1;
    }
    
    private static int ListPairs(ILuaState lua)
    {
        lua.CheckList(1);
        lua.PushCSharpFunction(ListNext);
        lua.PushValue(1);
        lua.PushNil();

        return 3;
    }

    private static void GetValue(LuaState L, int index, out StkId id)
    {
        if (!L.Index2Addr(index, out id))
            L.Error("Can't access variable");
    }
}