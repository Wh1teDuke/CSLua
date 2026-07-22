
// TODO

namespace CSLua.Lib;

public static class LuaIOLib
{
	public const string LIB_NAME = "io";

	public static NameFuncPair NameFuncPair => new(LIB_NAME, OpenLib);

	public static int OpenLib(LuaState lua)
	{
		ReadOnlySpan<NameFuncPair> define = 
		[
			new("close", 		IO_Close),
			new("flush", 		IO_Flush),
			new("input", 		IO_Input),
			new("lines", 		IO_Lines),
			new("open", 		IO_Open),
			new("output", 		IO_Output),
			new("popen", 		IO_Popen),
			new("read", 		IO_Read),
			new("tmpfile", 		IO_Tmpfile),
			new("type", 		IO_Type),
			new("write", 		IO_Write),
		];

		lua.NewLib(define);
		return 1;
	}

	private static int IO_Close(LuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Flush(LuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Input(LuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Lines(LuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Open(LuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Output(LuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Popen(LuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Read(LuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Tmpfile(LuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Type(LuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Write(LuaState lua)
	{
		// TODO
		return 0;
	}
}