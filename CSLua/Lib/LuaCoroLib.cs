
namespace CSLua.Lib;

public static class LuaCoroLib
{
	public const string LIB_NAME = "coroutine";
	
	public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);

	public static int OpenLib(ILuaState lua)
	{
		Span<NameFuncPair> define =
		[
			new("create", 	CO_Create),
			new("resume", 	CO_Resume),
			new("running", 	CO_Running),
			new("status", 	CO_Status),
			new("wrap", 	CO_Wrap),
			new("yield", 	CO_Yield),
		];

		lua.L_NewLib(define);
		return 1;
	}

	private static int CO_Create(ILuaState lua)
	{
		lua.L_CheckType(1, LuaType.LUA_TFUNCTION);
		ILuaState newLua = lua.NewThread();
		lua.PushValue(1); // Move function to top
		lua.XMove(newLua, 1); // Move function from lua to newLua
		return 1;
	}

	private static int AuxResume(ILuaState lua, ILuaState co, int nArg)
	{
		if (!co.CheckStack(nArg)) 
		{
			lua.PushString("Too many arguments to resume");
			return -1; // Error flag
		}
		if (co.Status == ThreadStatus.LUA_OK && co.GetTop() == 0)
		{
			lua.PushString("Cannot resume dead coroutine");
			return -1; // Error flag
		}
		lua.XMove(co, nArg);
		var status = co.Resume(lua, nArg);
		if (status is ThreadStatus.LUA_OK or ThreadStatus.LUA_YIELD)
		{
			var nRes = co.GetTop();
			if (!lua.CheckStack(nRes + 1)) 
			{
				co.Pop(nRes); // Remove results anyway;
				lua.PushString("Too many results to resume");
				return -1; // Error flag
			}
			co.XMove(lua, nRes); // Move yielded values
			return nRes;
		}

		co.XMove(lua, 1); // Move error message
		return -1;
	}

	private static int CO_Resume(ILuaState lua)
	{
		var co = lua.ToThread(1);
		lua.L_ArgCheck(co != null, 1, "coroutine expected");
		var r = AuxResume(lua, co!, lua.GetTop() - 1);
		if (r < 0)
		{
			lua.PushBoolean(false);
			lua.Insert(-2);
			return 2; // return false + error message
		}

		lua.PushBoolean(true);
		lua.Insert(-(r + 1));
		return r + 1; // return true + 'resume' returns
	}

	private static int CO_Running(ILuaState lua)
	{
		var isMain = lua.PushThread();
		lua.PushBoolean(isMain);
		return 2;
	}

	private static int CO_Status(ILuaState lua)
	{
		var co = (LuaState)lua.ToThread(1);
		lua.L_ArgCheck(co != null, 1, "coroutine expected");
		if ((LuaState)lua == co!)
			lua.PushString("running");
		// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
		else switch (co!.Status)
		{
			case ThreadStatus.LUA_YIELD:
				lua.PushString("suspended");
				break;
			case ThreadStatus.LUA_OK:
			{
				var _ = 0;
				if (co.GetStack(ref _, 0)) // Does it have frames?
					lua.PushString("normal");
				else if (co.GetTop() == 0)
					lua.PushString("dead");
				else
					lua.PushString("suspended");
				break;
			}
			default: // some error occurred
				lua.PushString("dead");
				break;
		}
		return 1;
	}

	private static int CO_AuxWrap(ILuaState lua)
	{
		var co = lua.ToThread(lua.UpValueIndex(1));
		var r = AuxResume(lua, co, lua.GetTop());
		if (r < 0)
		{
			if (lua.IsString(-1)) // Error object is a string?
			{
				lua.L_Where(1); // Add extra info
				lua.Insert(-2);
				lua.Concat(2);
			}
			lua.Error();
		}

		return r;
	}

	private static int CO_Wrap(ILuaState lua)
	{
		CO_Create(lua);
		lua.PushCSharpClosure(CO_AuxWrap, 1);
		return 1;
	}

	private static int CO_Yield(ILuaState lua) =>
		lua.Yield(lua.GetTop());
}