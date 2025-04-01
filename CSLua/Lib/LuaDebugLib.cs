
namespace CSLua.Lib;

public static class LuaDebugLib
{
	public const string LIB_NAME = "debug";
	
	public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);
	public static NameFuncPair SafeNameFuncPair => new (LIB_NAME, OpenSafeLib);

	public static int OpenLib(ILuaState lua)
	{
		Span<NameFuncPair> define =
		[
			new("traceback", 	DBG_Traceback),
			new("getinfo",		DBG_GetInfo),
			new("getupvalue",   DBG_GetUpvalue),
			new("setupvalue",   DBG_SetUpvalue),
		];

		lua.NewLib(define);
		return 1;
	}
	
	public static int OpenSafeLib(ILuaState lua)
	{
		Span<NameFuncPair> define =
		[
			new("traceback", 		DBG_Traceback),
			//new("getinfo",		DBG_GetInfo),
			//new("getupvalue",		DBG_GetUpvalue),
			//new("setupvalue",		DBG_SetUpvalue),
		];

		lua.NewLib(define);
		return 1;
	}

	private static int DBG_Traceback(ILuaState lua)
	{
		var L1 = lua.GetThread(out var arg);
		var msg = lua.ToString(arg + 1);
		
		if (msg == null && !lua.IsNoneOrNil(arg + 1)) /* non-string 'msg'? */
			lua.PushValue(arg + 1);
		else
		{
			var level = lua.OptInt(arg + 2, (lua == L1) ? 1 : 0);
			lua.Traceback(L1, msg, level);
		}

		return 1;
	}

	private static int DBG_GetInfo(ILuaState lua)
	{
		var L = (LuaState)lua;
		var L1 = lua.GetThread(out var arg);
		var what = L.OptString(arg + 2, "flnStu");
		var debug = new LuaDebug();
		
		if (L.Type(arg + 1) == LuaType.LUA_TNUMBER)
		{
			var level = L.ToInteger(arg + 1);
			if (!L.GetStack(debug, level))
			{
				L.PushNil();
				return 1;
			}
		}
		else if (L.Type(arg + 1) == LuaType.LUA_TSTRING)
			what = L.ToString(arg + 1);
		else if (L.Type(arg + 1) == LuaType.LUA_TFUNCTION)
		{
			what = ">" + what;
			lua.PushValue(arg + 1);
			lua.XMove(L1, 1);
		}
		else
			return lua.ArgError(arg + 1, "function or level expected");

		L.GetInfo(debug, what);

		var table = new LuaTable(L);
		table.Set("name", debug.Name);
		table.Set("currentline", debug.CurrentLine);
		table.Set("linedefined", debug.LineDefined);
		L.PushTable(table);
		
		return 1;
	}

	private static int DBG_GetUpvalue(ILuaState lua) => 
		AuxUpvalue(lua, 1);
	
	private static int DBG_SetUpvalue(ILuaState lua) => 
		AuxUpvalue(lua, 0);

	private static int AuxUpvalue(ILuaState lua, int get)
	{
		var n = lua.CheckInteger(2);
		lua.CheckType(1, LuaType.LUA_TFUNCTION);

		var name = get == 1 ? lua.GetUpValue(1, n) : lua.SetUpValue(1, n);
		if (name == null) return 0;

		lua.PushString(name);
		lua.Insert(-(get + 1));
		return get + 1;
	}
}