namespace CSLua;

public static class Lua
{
	public sealed class Debug
	{
		public string? 		Name;
		public string? 		NameWhat;
		public int 			ActiveCIIndex;
		public int			CurrentLine;
		public int			NumUps;
		public bool			IsVarArg;
		public int			NumParams;
		public bool			IsTailCall;
		public string?		Source;
		public int			LineDefined;
		public int			LastLineDefined;
		public string?		What;
		public string?		ShortSrc;
	}

	public delegate int CsDelegate(LuaState state);

	internal delegate void PFuncDelegate<T>(ref T ud) where T: struct;
	
	public static class Constants
	{
		public const int LUA_NOREF = -2;
		public const int LUA_REFNIL = -1;
	}
	
	public enum Type
	{
		LUA_TNONE = -1,
		LUA_TNIL = 0,
		LUA_TBOOLEAN = 1,
		LUA_TLIGHTUSERDATA = 2,
		LUA_TNUMBER = 3,
		LUA_TSTRING = 4,
		LUA_TTABLE = 5,
		LUA_TFUNCTION = 6,
		LUA_TUSERDATA = 7,
		LUA_TTHREAD = 8,

		LUA_TINT64 = 9,
		LUA_TLIST = 10,

		LUA_NUMTAGS = 11,

		LUA_TPROTO,
		LUA_TUPVAL,
		LUA_TDEADKEY,
	}
	
	public static string TypeName(Type t) =>
		t switch
		{
			Type.LUA_TNIL => "nil",
			Type.LUA_TBOOLEAN => "boolean",
			Type.LUA_TLIGHTUSERDATA => "lightuserdata",
			Type.LUA_TINT64 => "long",
			Type.LUA_TNUMBER => "number",
			Type.LUA_TSTRING => "string",
			Type.LUA_TTABLE => "table",
			Type.LUA_TLIST => "list",
			Type.LUA_TFUNCTION => "function",
			Type.LUA_TUSERDATA => "userdata",
			Type.LUA_TTHREAD => "thread",
			Type.LUA_TPROTO => "proto",
			Type.LUA_TUPVAL => "upval",
			_ => "no value"
		};

	public static LuaState New(GlobalState? g = null) => new (g);
}