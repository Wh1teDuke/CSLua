
// TODO

#define LUA_COMPAT_LOADERS


// ReSharper disable InconsistentNaming

namespace CSLua.Lib;

using StringBuilder = System.Text.StringBuilder;

public static class LuaPkgLib
{
	public const string LIB_NAME = "package";

	private const string CLIBS = "_CLIBS";

	private const string LUA_PATH = "LUA_PATH";
	private const string LUA_CPATH = "LUA_CPATH";
	private const string LUA_PATHSUFFIX = "_" + LuaDef.LUA_VERSION_MAJOR +
	                                      "_" + LuaDef.LUA_VERSION_MINOR;
	private const string LUA_PATHVERSION = LUA_PATH + LUA_PATHSUFFIX;
	private const string LUA_CPATHVERSION = LUA_CPATH + LUA_PATHSUFFIX;

	private const string LUA_PATH_DEFAULT = "?.lua;";
	private const string LUA_CPATH_DEFAULT = "?.dll;loadall.dll;";

	private const string LUA_PATH_SEP	= ";";
	private const string LUA_PATH_MARK	= "?";
	private const string LUA_EXEC_DIR	= "!";
	private const string LUA_IGMARK		= "-";

	private static readonly string LUA_LSUBSEP = LuaConf.LUA_DIRSEP;
	
	public static NameFuncPair NameFuncPair => new(LIB_NAME, OpenLib);

	public static int OpenLib(ILuaState lua)
	{
		// create `package' table
		Span<NameFuncPair> pkg_define = 
		[
			new("loadlib", 		PKG_LoadLib),
			new("searchpath",	PKG_SearchPath),
			new("seeall", 		PKG_SeeAll),
		];
		lua.L_NewLib(pkg_define);

		CreateSearchersTable(lua);
#if LUA_COMPAT_LOADERS
		lua.PushValue(-1 ); // Make a copy of 'searchers' table
		lua.SetField(-3, "loaders"); // Put it in field 'loaders'
#endif
		lua.SetField(-2, "searchers"); // Put it in field 'searchers'

		// Set field 'path'
		SetPath(lua, "path", LUA_PATHVERSION, LUA_PATH, LUA_PATH_DEFAULT );
		// Set field 'cpath'
		SetPath(lua, "cpath", LUA_CPATHVERSION, LUA_CPATH, LUA_CPATH_DEFAULT );

		// Store config information
		lua.PushString($"{LuaConf.LUA_DIRSEP}\n{LUA_PATH_SEP}\n{LUA_PATH_MARK}\n{LUA_EXEC_DIR}\n{LUA_IGMARK}\n");
		lua.SetField(-2, "config");

		// Set field 'loaded'
		lua.L_GetSubTable(LuaDef.LUA_REGISTRYINDEX, "_LOADED");
		lua.SetField(-2, "loaded");

		// Set field 'preload'
		lua.L_GetSubTable(LuaDef.LUA_REGISTRYINDEX, "_PRELOAD");
		lua.SetField(-2, "preload");

		lua.PushGlobalTable();
		lua.PushValue(-2); // set 'package' as upvalue for next lib

		Span<NameFuncPair> loadLibFuncs =
		[
			new("module",  LL_Module),
			new("require", LL_Require),
		];
		lua.L_SetFuncs(loadLibFuncs, 1); // open lib into global table
		lua.Pop(1); // pop global table

		return 1; // return `package' table
	}

	private static void CreateSearchersTable(ILuaState lua)
	{
		Span<CSharpFunctionDelegate> searchers = [SearcherPreload, SearcherLua];
		lua.CreateTable(searchers.Length, 0);
		for (var i = 0; i < searchers.Length; ++i)
		{
			lua.PushValue(-2); // set 'package' as upvalue for all searchers
			lua.PushCSharpClosure(searchers[i], 1);
			lua.RawSetI(-2, i + 1);
		}
	}

	private static void SetPath(
		ILuaState	lua
		, string 	fieldName
		, string 	envName1
		, string	envName2
		, string 	def
	)
	{
		lua.PushString(def);
		lua.SetField(-2, fieldName);
	}

	private static int SearcherPreload(ILuaState lua)
	{
		var name = lua.L_CheckString(1);
		lua.GetField(LuaDef.LUA_REGISTRYINDEX, "_PRELOAD");
		lua.GetField(-1, name);
		if (lua.IsNil(-1)) // Not found?
			lua.PushString($"\n\tno field package.preload['{name}']");
		return 1;
	}

	private static bool Readable(ILuaState state, string filename) => 
		LuaFile.Readable(state.BaseFolder, filename);

	private static bool PushNextTemplate(
		ILuaState lua, string path, ref int pos)
	{
		while (pos < path.Length && path[pos] == LUA_PATH_SEP[0])
			pos++; // skip separators
		if (pos >= path.Length)
			return false;
		var end = pos+1;
		while (end < path.Length && path[end] != LUA_PATH_SEP[0])
			end++;

		var template = path.Substring(pos, end - pos);
		lua.PushString(template);

		pos = end;
		return true;
	}

	private static string? SearchPath(
		ILuaState lua, string name, string path, string sep, string dirsep)
	{
		StringBuilder? sb = null; // to build error message
		
		if (!string.IsNullOrEmpty(sep))
			name = name.Replace(sep, dirsep); // '.' -> '/'

		var pos = 0;
		while (PushNextTemplate(lua, path, ref pos))
		{
			var template = lua.ToString(-1)!;
			var filename = template.Replace(LUA_PATH_MARK, name);
			lua.Remove(-1); // remove path template
			if (Readable(lua, filename)) // does file exist and is readable?
				return filename; // return that file name
			lua.PushString($"\n\tno file '{filename}'");
			lua.Remove(-2); // remove file name
			sb ??= new StringBuilder();
			sb.Append(lua.ToString(-1)); // Concatenate error msg. entry
		}
		lua.PushString(sb?.ToString() ?? string.Empty); // Create error message
		return null;
	}

	private static string? FindFile(
		ILuaState lua, string name, string pname, string dirsep)
	{
		lua.GetField(lua.UpValueIndex(1), pname);
		var path = lua.ToString(-1);
		if (path == null)
			lua.L_Error("'package.{0}' must be a string", pname);
		return SearchPath(lua, name, path!, ".", dirsep);
	}

	private static int CheckLoad(
		ILuaState lua, bool stat, string filename)
	{
		if (stat) // module loaded successfully?
		{
			lua.PushString(filename); // will be 2nd arg to module
			return 2; // return open function and file name
		}

		return lua.L_Error(
			"Error loading module '{0}' from file '{1}':\n\t{2}",
			lua.ToString(1), filename, lua.ToString(-1));
	}

	private static int SearcherLua(ILuaState lua)
	{
		var name = lua.L_CheckString(1);
		var filename = FindFile(lua, name, "path", LUA_LSUBSEP);
		if (filename == null) return 1;
		return CheckLoad(lua,
			(lua.L_LoadFile(filename) == ThreadStatus.LUA_OK),
			filename);
	}

	private static int LL_Module(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static void FindLoader(ILuaState lua, string name)
	{
		// will be at index 3
		lua.GetField(lua.UpValueIndex(1), "searchers");
		if(!lua.IsTable(3))
			lua.L_Error("'package.searchers' must be a table");

		var sb = new StringBuilder();
		// iterator over available searchers to find a loader
		for (var i = 1; ; ++i)
		{
			lua.RawGetI(3, i); // get a searcher
			if (lua.IsNil(-1)) // no more searchers?
			{
				lua.Pop(1); // remove nil
				lua.PushString(sb.ToString());
				lua.L_Error("Module '{0}' not found:{1}",
					name, lua.ToString(-1));
				return;
			}

			lua.PushString(name);
			lua.Call(1, 2); // call it
			if (lua.IsFunction(-2)) // did it find a loader
				return; // module loader found
			if (lua.IsString(-2)) // searcher returned error message?
			{
				lua.Pop(1); // return extra return
				sb.Append(lua.ToString(-1));
			}
			else
				lua.Pop(2); // remove both returns
		}
	}

	private static int LL_Require(ILuaState lua)
	{
		var name = lua.L_CheckString(1);
		lua.SetTop(1);
		// _LOADED table will be at index 2
		lua.GetField(LuaDef.LUA_REGISTRYINDEX, "_LOADED");
		// _LOADED[name]
		lua.GetField(2, name);
		// is it there?
		if (lua.ToBoolean(-1))
			return 1; // package is already loaded
		// else must load package
		// remove `GetField' result
		lua.Pop(1);
		FindLoader(lua, name);
		lua.PushString(name); // pass name as arg to module loader
		lua.Insert(-2); // name is 1st arg (before search data)
		lua.Call(2, 1); // run loader to load module
		if (!lua.IsNil(-1)) // non-nil return?
			lua.SetField(2, name); // _LOADED[name] = returned value
		lua.GetField(2, name);
		if (lua.IsNil(-1)) // module did not set a value?
		{
			lua.PushBoolean(true); // use true as result
			lua.PushValue(-1); // extra copy to be returned
			lua.SetField(2, name); // _LOADED[name] = true
		}
		return 1;
	}

	private static int PKG_LoadLib(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int PKG_SearchPath(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int PKG_SeeAll(ILuaState lua)
	{
		// TODO
		return 0;
	}
}