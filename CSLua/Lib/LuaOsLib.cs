
using System.Diagnostics;

namespace CSLua.Lib;

public static class LuaOSLib
{
	public const string LIB_NAME = "os";
	
	public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);
	public static NameFuncPair SafeNameFuncPair => new (LIB_NAME, OpenSafeLib);

	public static int OpenLib(ILuaState lua)
	{
		Span<NameFuncPair> define = 
		[
			new("clock", 		OS_Clock),
			new("difftime",		OS_DiffTime),
			new("sleep",		OS_Sleep),
			new("remove",       OS_Remove),
			new("rename",       OS_Rename),
			new("exit",			OS_Exit),
			new("setlocale",	OS_SetLocale),
		];

		lua.L_NewLib(define);
		return 1;
	}
	
	public static int OpenSafeLib(ILuaState lua)
	{
		Span<NameFuncPair> define = 
		[
			new("clock", 		OS_Clock),
			new("difftime",		OS_DiffTime),
			//new("sleep",		OS_Sleep),
			//new("remove",     OS_Remove),
			//new("rename",     OS_Rename),
			//new("exit",		OS_Exit),
		];

		lua.L_NewLib(define);
		return 1;
	}
	
	private static int OS_Clock(ILuaState lua)
	{
		lua.PushNumber(
			Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds);
		return 1;
	}
	
	private static int OS_DiffTime(ILuaState lua)
	{
		var t1 = lua.ToNumber(1);
		var t2 = lua.ToNumber(1);
		lua.PushNumber(t2 - t1);
		return 1;
	}
		
	private static int OS_Sleep(ILuaState lua)
	{
		Thread.Sleep(lua.ToInteger(1));
		return 0;
	}
	
	private static int OS_Rename(ILuaState lua)
	{
		lua.L_CheckType(1, LuaType.LUA_TSTRING);
		lua.L_CheckType(2, LuaType.LUA_TSTRING);
		var oldName = lua.ToString(1);
		var newName = lua.ToString(2);

		try
		{
			File.Move(oldName, newName);
			lua.PushBoolean(true);
			return 1;
		}
		catch (Exception ex)
		{
			lua.PushNil();
			lua.PushString(ex.Message);
			return 2;
		}
	}
	
	private static int OS_Remove(ILuaState lua)
	{
		lua.L_CheckType(1, LuaType.LUA_TSTRING);
		var fileName = lua.ToString(1);

		try
		{
			File.Delete(fileName);
			lua.PushBoolean(true);
			return 1;
		}
		catch (Exception e)
		{
			lua.PushNil();
			lua.PushString(e.Message);
			return 2;
		}
	}

	private static int OS_Exit(ILuaState lua)
	{
		int? code = null;
		var success = true;
		if (lua.GetTop() >= 1)
		{
			if (lua.IsBool(1))
				success = lua.ToBoolean(1);
			else
				code = lua.ToInteger(1);
		}
		
		if (code is {} c)
			Environment.Exit(c);
		if (success)
			Environment.Exit(0);
		Environment.Exit(1);
		return 0;
	}

	private static int OS_SetLocale(ILuaState lua)
	{
		lua.PushNil();
		return 1;
	}
}