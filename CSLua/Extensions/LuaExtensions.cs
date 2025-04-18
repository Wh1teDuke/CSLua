using CSLua.Util;

// ReSharper disable InconsistentNaming

namespace CSLua.Extensions;

public static class LuaExtensions
{
    public static int PopInteger(this ILuaState L)
    {
        var i = L.ToInteger(-1);
        L.Pop(1);
        return i;
    }
	
    public static long PopInt64(this ILuaState L)
    {
        var i = L.ToInt64(-1);
        L.Pop(1);
        return i;
    }
	
    public static double PopNumber(this ILuaState L)
    {
        var i = L.ToNumber(-1);
        L.Pop(1);
        return i;
    }

    public static bool? PopBool(this ILuaState L)
    {
        var i = L.ToBoolean(-1);
        L.Pop(1);
        return i;
    }

    public static string? PopString(this ILuaState L)
    {
        var i = L.ToString(-1);
        if (i != null) L.Pop(1);
        return i;
    }
    
    public static LuaTable PopTable(this ILuaState L)
    {
        var i = (LuaTable)L.ToObject(-1);
        L.Pop(1);
        return i;
    }

    public static object? PopUserData(this ILuaState L)
    {
        var i = L.ToUserData(-1);
        if (i != null) L.Pop(1);
        return i;
    }
    
    public static ILuaState? PopThread(this ILuaState L)
    {
        var t = L.ToThread(-1);
        if (t != null) L.Pop(1);
        return t;
    }
    
    public static LuaClosure? PopLuaClosure(this ILuaState L)
    {
        var t = L.ToLuaClosure(-1);
        if (t != null) L.Pop(1);
        return t;
    }
    
    public static void SetGlobalInteger(this ILuaState L, string name, int i)
    {
        L.PushInteger(i);
        L.SetGlobal(name);
    }

    public static int? GetGlobalInteger(this ILuaState L, string name)
    {
        L.GetGlobal(name);
        return L.PopInteger();
    }
	
    public static void SetGlobalNumber(this ILuaState L, string name, double i)
    {
        L.PushNumber(i);
        L.SetGlobal(name);
    }

    public static double GetGlobalNumber(this ILuaState L, string name)
    {
        L.GetGlobal(name);
        return L.PopNumber();
    }
    
    public static bool? GetGlobalBool(this ILuaState L, string name)
    {
        L.GetGlobal(name);
        return L.PopBool();
    }
    
    public static bool? TryGetBool(this ILuaState L, int index) => 
        !L.IsBool(index) ? null : L.ToBoolean(-1);

    public static bool? TryPopBool(this ILuaState L)
    {
        var r = TryGetBool(L, -1);
        if (r.HasValue) L.Pop(1);
        return r;
    }
    
    public static double? TryGetNumber(this ILuaState L, int index)
    {
        return !L.IsNumber(index) ? null : L.ToNumber(-1);
    }
    
    public static double? TryPopNumber(this ILuaState L)
    {
        var r = TryGetNumber(L, -1);
        if (r.HasValue) L.Pop(1);
        return r;
    }

    public static bool PrintAnyError(this ILuaState L) => 
        PrintAnyError(L, L.Status);
    
    public static string PopErrorMsg(this ILuaState L)
    {
        var err = L.ToStringX(-1);
        L.Pop(-1);
        return err;
    }

    public static bool PrintAnyError(this ILuaState L, ThreadStatus status)
    {
        if (status != ThreadStatus.LUA_OK)
        {
            PrintError(L);
            return true;
        }

        return false;
    }
    
    public static void PrintError(this ILuaState L)
    {
        var err = PopErrorMsg(L);
        Console.WriteLine("Error!: " + err);
    }

    public static void RegisterFunction(
        this ILua L, string name, CsDelegate callBack)
    {
        L.PushCsDelegate(callBack);
        L.SetGlobal(name);
    }

    public static void DeleteGlobal(this ILua L, string name)
    {
        L.GetGlobal(name);
        L.PushNil();
        L.SetGlobal(name);
        L.Pop(-1);
    }
    
    public static void DeleteField(this ILua L, string name, string field)
    {
        L.GetGlobal(name);
        L.PushNil();
        L.SetField(-2, field);
        L.Pop(-1);
    }
    
    
    private static void EvalX(
        this ILuaState L, string s, BaseClosure? errorHandler = null)
    {
        ThreadStatus status;
		
        if (errorHandler == null)
            status = L.DoString(s);

        else
        {
            L.PushClosure(errorHandler);
            var errIndex = L.GetTop();
            status = L.LoadString(s);

            if (status == ThreadStatus.LUA_OK) 
                status = L.PCall(0, LuaDef.LUA_MULTRET, errIndex);
        }
		
        if (status == ThreadStatus.LUA_OK) return;

        var msg = L.ToString(-1)!;
        L.Pop(1);
        throw new LuaRuntimeException(status, msg);
    }
    
    public static void Eval(
            this ILuaState L, string s, BaseClosure? onError = null) =>
        L.EvalX(s, onError);
	
    public static void Eval(
            this ILuaState L, string s, CsDelegate onError) =>
        L.EvalX(s, new CsClosure(onError));
    
    private static void PushClosure(this ILua L, BaseClosure c)
    {
        if (c is LuaClosure closure) L.PushLuaClosure(closure);
        else L.PushCsClosure((CsClosure)c);
    }
    
    public static bool TestStack(this ILua L, ReadOnlySpan<LuaType> args)
    {
        if (L.GetTop() != args.Length) return false;
        var i = 1;
        foreach (var arg in args)
        {
            if (L.Type(i++) != arg) return false;
        }

        return true;
    }
}