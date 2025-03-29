
// TODO

namespace CSLua.Lib;

public static class  LuaIOLib
{
	public const string LIB_NAME = "io";

	public static NameFuncPair NameFuncPair => new(LIB_NAME, OpenLib);

	public static int OpenLib(ILuaState lua)
	{
		Span<NameFuncPair> define = 
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

		lua.L_NewLib(define);
		return 1;
	}

	private static int IO_Close(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Flush(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Input(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Lines(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Open(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Output(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Popen(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Read(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Tmpfile(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Type(ILuaState lua)
	{
		// TODO
		return 0;
	}

	private static int IO_Write(ILuaState lua)
	{
		// TODO
		return 0;
	}
}