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
		public string		Source;
		public int			LineDefined;
		public int			LastLineDefined;
		public string		What;
		public string		ShortSrc;
	}

	public delegate int CsDelegate(LuaState state);

	internal delegate void PFuncDelegate<T>(ref T ud) where T: struct;
}