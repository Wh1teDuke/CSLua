
namespace CSLua.Lib;

public static class LuaDebugLib
{
	public const string LIB_NAME = "debug";
	
	public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);
	public static NameFuncPair SafeNameFuncPair => new (LIB_NAME, OpenSafeLib);

	public static int OpenLib(LuaState lua)
	{
		ReadOnlySpan<NameFuncPair> define =
		[
			new("traceback", 	DBG_Traceback),
			new("getinfo",		DBG_GetInfo),
			new("getupvalue",   DBG_GetUpvalue),
			new("setupvalue",   DBG_SetUpvalue),
		];

		lua.NewLib(define);
		return 1;
	}
	
	public static int OpenSafeLib(LuaState lua)
	{
		ReadOnlySpan<NameFuncPair> define =
		[
			new("traceback", 		DBG_Traceback),
			//new("getinfo",		DBG_GetInfo),
			//new("getupvalue",		DBG_GetUpvalue),
			//new("setupvalue",		DBG_SetUpvalue),
		];

		lua.NewLib(define);
		return 1;
	}

	private static int DBG_Traceback(LuaState lua)
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

	private static int DBG_GetInfo(LuaState lua)
	{
		var L = lua;
		var L1 = lua.GetThread(out var arg);
		var options = L.OptString(arg + 2, "flnSrtu");
		var debug = new Lua.Debug();
		
		if (L.Type(arg + 1) == Lua.Type.LUA_TNUMBER)
		{
			var level = L.ToInteger(arg + 1);
			if (!L.GetStack(debug, level))
			{
				L.PushNil();
				return 1;
			}
		}
		else if (L.AsString(arg + 1) is {} str)
			options = str;
		else if (L.Type(arg + 1) == Lua.Type.LUA_TFUNCTION)
		{
			options = ">" + options;
			lua.PushValue(arg + 1);
			lua.XMove(L1, 1);
		}
		else
			return lua.ArgError(arg + 1, "function or level expected");

		L.GetInfo(debug, options);

		var table = new LuaTable(L);

		if (options.Contains('S'))
		{
			if (debug.Source != null)
				table.Set("source", debug.Source);
			if (debug.ShortSrc != null)
				table.Set("short_src",  debug.ShortSrc);
			table.Set("linedefined", debug.LineDefined);
			table.Set("lastlinedefined", debug.LastLineDefined);
			if (debug.What != null)
				table.Set("what", debug.What);
		}
		
		if (options.Contains('l'))
			table.Set("currentline", debug.CurrentLine);

		if (options.Contains('u'))
		{
			table.Set("nups", debug.NumUps);
			table.Set("nparams", debug.NumParams);
			table.Set("isvararg", debug.IsVarArg);
		}

		if (options.Contains('n'))
		{
			if (debug.Name != null)
				table.Set("name", debug.Name);
			if (debug.NameWhat != null)
				table.Set("namewhat", debug.NameWhat);
		}

		if (options.Contains('r'))
		{
			// TODO this is new
		}
		
		if (options.Contains('t'))
		{
			table.Set("istailcall", debug.IsTailCall);
			// TODO settabsi(L, "extraargs", ar.extraargs);
		}

		if (options.Contains('L'))
		{
			// TODO	
		}
		
		if (options.Contains('f'))
		{
			// TODO	
		}

		L.PushTable(table);
		
		return 1;
	}

	private static int DBG_GetUpvalue(LuaState lua) => 
		AuxUpvalue(lua, 1);
	
	private static int DBG_SetUpvalue(LuaState lua) => 
		AuxUpvalue(lua, 0);

	private static int AuxUpvalue(LuaState lua, int get)
	{
		var n = lua.CheckInteger(2);
		lua.CheckType(1, Lua.Type.LUA_TFUNCTION);

		var name = get == 1 ? lua.GetUpValue(1, n) : lua.SetUpValue(1, n);
		if (name == null) return 0;

		lua.PushString(name);
		lua.Insert(-(get + 1));
		return get + 1;
	}
}